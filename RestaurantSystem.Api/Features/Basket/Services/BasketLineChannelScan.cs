using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Features.Basket.Dtos.Requests;
using RestaurantSystem.Api.Features.Catalog;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Basket.Services;

/// <summary>
/// Resolves the channel availability of a basket LINE — the root row plus everything it puts on the
/// kitchen ticket with it: bundle children (recursively) and top-level side items.
/// </summary>
/// <remarks>
/// Shared by <see cref="BasketChannelService"/> (the order-type switch) and
/// <see cref="AnonymousBasketMerger"/> (the login merge), which had the same root-only scan and the
/// same gap (§9.15). Both must agree with <c>OrderChannelGuard</c>, which is the final enforcement at
/// order creation: it walks <c>CreateOrderItemDto.ChildItems</c>, and
/// <c>BasketToOrderTranslator</c> puts BOTH bundle children and side items there. Anything this class
/// fails to see is refused at the till instead — the guest chose, was told nothing, and gets a 400.
/// <para>
/// ⚠️ <b>The load-bearing part is <see cref="LoadProductsAsync"/>'s include.</b>
/// <see cref="OrderTypeAvailability.EffectiveMask"/> resolves inheritance through
/// <c>ProductCategories → Category</c>, and an UNLOADED collection reads as UNRESTRICTED rather than
/// throwing. A scan that widens which products it checks but not which columns it loads is
/// permanently permissive while looking thorough — the #231/#236/#237/#241/§9.3 class. That is why
/// the load lives here and not at the call sites.
/// </para>
/// </remarks>
internal static class BasketLineChannelScan
{
    /// <summary>
    /// Every product id the given root lines put on the ticket: the roots, their descendants, and
    /// their side items.
    /// </summary>
    public static HashSet<Guid> CollectProductIds(
        IEnumerable<BasketItem> roots,
        IReadOnlyCollection<BasketItem> allItems)
    {
        var ids = new HashSet<Guid>();

        foreach (var root in roots)
        {
            foreach (var line in WithDescendants(root, allItems))
            {
                if (line.ProductId is { } productId)
                {
                    ids.Add(productId);
                }

                foreach (var sideId in SideItemProductIds(line))
                {
                    ids.Add(sideId);
                }
            }
        }

        return ids;
    }

    /// <summary>Loads the products a scan needs, WITH the include inheritance depends on.</summary>
    public static async Task<Dictionary<Guid, Product>> LoadProductsAsync(
        ApplicationDbContext context,
        IReadOnlyCollection<Guid> productIds,
        CancellationToken cancellationToken)
    {
        if (productIds.Count == 0)
        {
            return [];
        }

        return await context.Products
            .AsNoTracking()
            .Include(p => p.ProductCategories)
                .ThenInclude(pc => pc.Category)
            .Where(p => productIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, cancellationToken);
    }

    /// <summary>
    /// The channel mask governing a whole line: the INTERSECTION of its own mask and those of every
    /// component it carries. A line is orderable only where all of its parts are.
    /// </summary>
    /// <remarks>
    /// <c>null</c> means unrestricted, so a null contributes nothing to the intersection and an
    /// all-null line stays null. This generalises the previous root-only behaviour rather than
    /// changing it: a line with no children and no sides intersects exactly one mask — its own.
    /// <para>
    /// Reporting stays at ROOT granularity even when the blocked part is a component, and that is
    /// deliberate. A child row could be named (it has a <c>BasketItemId</c>) but removing it would
    /// leave a bundle with an unfilled required section, and a side item has no row id at all — it
    /// lives in <c>SelectedSideItemsJson</c>. So the guest is offered the whole line, which is also
    /// what <c>SetOrderTypeAsync</c>'s removal path already deletes correctly (children first).
    /// </para>
    /// </remarks>
    public static int? LineMask(
        BasketItem root,
        IReadOnlyCollection<BasketItem> allItems,
        IReadOnlyDictionary<Guid, Product> products)
    {
        int? combined = null;

        foreach (var line in WithDescendants(root, allItems))
        {
            foreach (var productId in ProductIdsOf(line))
            {
                // A product the query did not return cannot be resolved, so it cannot narrow the
                // mask. Unresolvable side ids are already impossible on the write path —
                // BasketItemFactory persists only sides that RESOLVED. A missing ROOT product is
                // reachable, though (DeleteProductCommand soft-deletes without touching basket
                // rows), and the line is still scanned rather than skipped: its children resolve
                // fine, and OrderChannelGuard would judge them at checkout whatever happens here.
                if (!products.TryGetValue(productId, out var product))
                {
                    continue;
                }

                var mask = OrderTypeAvailability.EffectiveMask(product);
                if (mask is null)
                {
                    continue;
                }

                combined = combined is null ? mask : combined & mask;
            }
        }

        return combined;
    }

    /// <summary>A root line and every bundle child beneath it, at any depth.</summary>
    /// <remarks>
    /// Walks <c>ParentBasketItemId</c> over the flat loaded set rather than the
    /// <c>ChildBasketItems</c> navigation, because the callers load baskets through
    /// <c>FindTrackedBasketWithItemsAsync</c> (<c>.Items</c> only) — the navigation is not populated
    /// and would silently enumerate empty.
    /// <para>
    /// ⚠️ <b>This recurses further than its consumers can act.</b> Baskets are only ever built one
    /// level deep (<c>BasketItemFactory.BuildMenuItemAsync</c> is the sole writer of child rows), and
    /// both downstream paths assume that: <c>BasketChannelService</c>'s removal collects children
    /// whose parent is doomed but not GRANDchildren (which the missing cascade would then PROMOTE to
    /// top-level lines, onto the kitchen ticket), and <c>AnonymousBasketMerger</c> re-homes one level
    /// (grandchildren would be orphaned under the soft-deleted basket). Detecting a conflict at depth
    /// &gt; 1 is therefore safe, but ACTING on one is not — if nesting ever becomes real, those two
    /// paths must recurse before this remark is deleted.
    /// </para>
    /// </remarks>
    private static IEnumerable<BasketItem> WithDescendants(
        BasketItem root,
        IReadOnlyCollection<BasketItem> allItems)
    {
        yield return root;

        foreach (var child in allItems.Where(i => i.ParentBasketItemId == root.Id))
        {
            foreach (var descendant in WithDescendants(child, allItems))
            {
                yield return descendant;
            }
        }
    }

    private static IEnumerable<Guid> ProductIdsOf(BasketItem line)
    {
        if (line.ProductId is { } productId)
        {
            yield return productId;
        }

        foreach (var sideId in SideItemProductIds(line))
        {
            yield return sideId;
        }
    }

    /// <summary>
    /// The product ids in a line's <c>SelectedSideItemsJson</c>. <c>SelectedSideItemDto.Id</c> IS a
    /// product id — <c>BasketItemFactory</c> resolves it against <c>Products</c> — so no extra
    /// lookup is needed.
    /// </summary>
    /// <remarks>
    /// Deliberately NOT wrapped in a try/catch. The column is written only by
    /// <c>BasketItemFactory</c>, so unparseable content means corruption, and swallowing it here
    /// would make the scan silently permissive — the precise failure this whole feature keeps
    /// hitting. A <c>JsonException</c> surfacing as a 500 is loud and fixable; a blocked component
    /// waved through is neither. Mirrors <c>BasketService.SameSideItems</c>, which also does not
    /// catch, including its <c>OfType</c> guard against null array elements.
    /// </remarks>
    private static IEnumerable<Guid> SideItemProductIds(BasketItem line)
    {
        if (string.IsNullOrEmpty(line.SelectedSideItemsJson))
        {
            return [];
        }

        var sides = JsonSerializer.Deserialize<List<SelectedSideItemDto>>(line.SelectedSideItemsJson);

        return sides is null
            ? []
            : sides.OfType<SelectedSideItemDto>().Select(s => s.Id);
    }
}
