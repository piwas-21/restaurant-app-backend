using System.Text.Json;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Basket.Dtos;
using RestaurantSystem.Api.Features.Basket.Interfaces;
using RestaurantSystem.Api.Features.Catalog;
using RestaurantSystem.Domain.Common;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Basket.Services;

/// <summary>
/// Default <see cref="IAnonymousBasketMerger"/>. Merges the anonymous (session) basket
/// into the user's basket at login. Audit stamping uses
/// <c>ICurrentUserService.GetAuditIdentifier()</c> per CLAUDE.md §5.13. This merge runs from
/// the on-login event handler, where the request principal is not yet the logging-in user,
/// so the audit identifier resolves to the project-standard "System" for this automatic
/// operation — an audit-string-only detail with no functional impact.
///
/// Baskets are loaded WITH change tracking (<see cref="IBasketRepository.FindTrackedBasketWithItemsAsync"/>)
/// so the adopt (UserId) and soft-delete (IsDeleted) mutations actually persist — the prior
/// AsNoTracking load silently dropped them (PR #89 review). The result is mapped from a fresh
/// full-graph <see cref="IBasketRepository.FindBasketAsync"/> load so the returned DTO carries
/// the product/variation/menu graph the tracked Items-only load omits.
///
/// When a duplicate's quantity is merged into an existing user item, the now-redundant anonymous
/// row is hard-deleted — but ONLY for standalone leaf items. BasketItem is not soft-delete-aware,
/// so a Remove is a real DELETE, and the self-referencing parent/child FK is Restrict; deleting a
/// menu-bundle parent (or a child whose parent is being moved) would break that FK. A merged bundle's
/// rows are therefore left under the soft-deleted anonymous basket — invisible and not double-counted.
/// Since #313 that only happens for a bundle the guest built IDENTICALLY on both sides, because
/// <see cref="BasketLineCustomization"/> compares composition and a differing build no longer matches;
/// it re-homes as its own line instead of being abandoned.
/// </summary>
public class AnonymousBasketMerger : IAnonymousBasketMerger
{
    private readonly ApplicationDbContext _context;
    private readonly IBasketRepository _basketRepository;
    private readonly IBasketMappingService _basketMappingService;
    private readonly ICurrentUserService _currentUserService;
    private readonly ILogger<AnonymousBasketMerger> _logger;

    public AnonymousBasketMerger(
        ApplicationDbContext context,
        IBasketRepository basketRepository,
        IBasketMappingService basketMappingService,
        ICurrentUserService currentUserService,
        ILogger<AnonymousBasketMerger> logger)
    {
        _context = context;
        _basketRepository = basketRepository;
        _basketMappingService = basketMappingService;
        _currentUserService = currentUserService;
        _logger = logger;
    }

    public async Task<BasketDto> MergeAsync(string sessionId, Guid userId)
    {
        // Tracked loads: the header mutations below (UserId, IsDeleted) must persist.
        var anonymousBasket = await _basketRepository.FindTrackedBasketWithItemsAsync(sessionId, null);
        var userBasket = await _basketRepository.FindTrackedBasketWithItemsAsync(null, userId);

        if (anonymousBasket == null)
        {
            // Nothing to merge: return the user's basket (or a fresh one), mapped from the
            // full-graph load so the DTO carries product/variation/menu details.
            // Pass null for sessionId so the created basket is keyed on userId only and
            // does not inherit the anonymous session ID (which could cause future session
            // lookups to incorrectly match this user's basket).
            return await MapByUserAsync(userId)
                ?? await _basketMappingService.MapAsync(await _basketRepository.GetOrCreateBasketAsync(null, userId));
        }

        if (userBasket == null)
        {
            // Adopt the anonymous basket as the user's. Tracked, so this persists.
            anonymousBasket.UserId = userId;
            anonymousBasket.UpdatedAt = DateTime.UtcNow;
            anonymousBasket.UpdatedBy = _currentUserService.GetAuditIdentifier();
            await _context.SaveChangesAsync();

            return await MapByUserAsync(userId)
                ?? throw new BadRequestException("Failed to retrieve basket");
        }

        // Merge anonymous items into the user basket. Both item sets are already loaded
        // (tracked) by the calls above, so the matching is in-memory — no per-item query.
        // Snapshot first: removing a merged duplicate (below) mutates anonymousBasket.Items
        // via EF relationship fix-up, which would otherwise break the enumeration.
        var anonymousItems = anonymousBasket.Items.ToList();

        // Rows actually moved into the user basket. Tracked explicitly because EF's navigation
        // fix-up has not run yet at the point we need to inspect them (see
        // ResetOrderTypeIfMergedItemsConflictAsync).
        var rehomed = new List<Domain.Entities.BasketItem>();

        // Items that are menu-bundle parents within the anonymous basket (some other item
        // points at them). Used to keep the duplicate-removal below to safe standalone leaves.
        // Children by parent, for both sides. A bundle PARENT stores none of its own composition, so
        // the comparison below cannot see what the guest chose without them (#313).
        var anonymousChildren = ChildrenByParent(anonymousItems);
        var userChildren = ChildrenByParent(userBasket.Items);

        var parentItemIds = anonymousItems
            .Where(i => i.ParentBasketItemId.HasValue)
            .Select(i => i.ParentBasketItemId!.Value)
            .ToHashSet();

        // Only iterate root items (ParentBasketItemId == null). Child items of bundles must
        // not be matched flatly — they share ProductIds with standalone items and would cause
        // incorrect quantity merges. When a distinct root bundle is moved, its children are
        // re-homed explicitly below.
        foreach (var item in anonymousItems.Where(i => i.ParentBasketItemId == null))
        {
            // Identity AND customization (#313) — the rule and the measurements are in
            // BasketLineCustomization. Worth naming HERE is the consequence unique to this method: a
            // row that does not match re-homes, and a re-homed row enters `rehomed`, so it now reaches
            // ResetOrderTypeIfMergedItemsConflictAsync — which clears the basket's order type on an
            // unreadable side-item column. Previously such a row was merged and hard-deleted, so it
            // never reached the scan. An unreadable line therefore costs the guest a re-pick of
            // DineIn/Takeaway on top of a duplicate row, which is still better than the old behaviour
            // of destroying the row silently.
            //
            // `incoming is not null` sits FIRST in the predicate: without it an unreadable USER row is
            // re-parsed, and its warning re-logged, once per anonymous root for an answer already known.
            var incoming = Customization(item, anonymousChildren);

            var existingItem = userBasket.Items.FirstOrDefault(bi =>
                bi.ParentBasketItemId == null &&
                bi.ProductId == item.ProductId &&
                bi.ProductVariationId == item.ProductVariationId &&
                incoming is not null &&
                BasketLineCustomization.AreSame(Customization(bi, userChildren), incoming));

            if (existingItem != null)
            {
                // The SECOND site where a bundle parent's quantity moves (#305). The match above
                // keys on ProductId + variation and excludes only CHILD rows, so a bundle the guest
                // holds in both baskets lands here exactly like a standalone product — and the user
                // basket's children would otherwise keep the count they were built with.
                //
                // Children come from the flat Items list, not from existingItem.ChildBasketItems:
                // FindTrackedBasketWithItemsAsync includes `b.Items` only, which already contains
                // every child row, and reading them this way does not depend on EF relationship
                // fix-up having populated the navigation.
                var existingChildren = userBasket.Items
                    .Where(i => i.ParentBasketItemId == existingItem.Id)
                    .ToList();

                BundleChildQuantityScaler.Rescale(
                    existingChildren,
                    existingItem.Quantity,
                    existingItem.Quantity + item.Quantity,
                    _currentUserService.GetAuditIdentifier());

                existingItem.Quantity += item.Quantity;

                // THE MONEY (#308). This was a flat `(UnitPrice + CustomizationPrice) * Quantity`,
                // justified in a comment from BuildRegularItemAsync — true of a regular item, and a
                // DOUBLE CHARGE for a bundle, whose UnitPrice already contains its customization.
                // Measured at 57.00 where 48.00 is correct: 9.00 on one line, at every login where
                // the same bundle sits in both baskets. Rule and rationale: BasketLineTotal.
                // `existingChildren` is read from the flat Items list just above, so this does not
                // depend on EF relationship fix-up having populated the navigation.
                existingItem.ItemTotal = BasketLineTotal.ForRoot(existingItem, existingChildren.Count);
                existingItem.UpdatedAt = DateTime.UtcNow;
                existingItem.UpdatedBy = _currentUserService.GetAuditIdentifier();

                // The anonymous duplicate is now redundant. Hard-delete it ONLY when it is a
                // standalone leaf (not a bundle parent) — see the class summary for why
                // bundle-entangled rows are left for the soft-deleted basket to carry.
                bool isStandaloneLeaf = !parentItemIds.Contains(item.Id);
                if (isStandaloneLeaf)
                {
                    _context.BasketItems.Remove(item);
                }
            }
            else
            {
                item.BasketId = userBasket.Id;
                item.UpdatedAt = DateTime.UtcNow;
                item.UpdatedBy = _currentUserService.GetAuditIdentifier();
                rehomed.Add(item);

                // Also move any child items belonging to this bundle — without this they
                // would be left orphaned under the soft-deleted anonymous basket.
                var children = anonymousItems.Where(c => c.ParentBasketItemId == item.Id);
                foreach (var child in children)
                {
                    child.BasketId = userBasket.Id;
                    child.UpdatedAt = DateTime.UtcNow;
                    child.UpdatedBy = _currentUserService.GetAuditIdentifier();
                }
            }
        }

        // Soft-delete the anonymous basket. Tracked, so this persists.
        anonymousBasket.IsDeleted = true;
        anonymousBasket.DeletedAt = DateTime.UtcNow;
        anonymousBasket.DeletedBy = _currentUserService.GetAuditIdentifier();

        await MergedBasketChannelReset.ApplyAsync(_context, _logger, userBasket, rehomed, anonymousItems);

        await _context.SaveChangesAsync();
        await _basketRepository.RecalculateTotalsAsync(userBasket.Id);

        return await MapByUserAsync(userId)
            ?? throw new BadRequestException("Failed to retrieve basket");
    }

    private static Dictionary<Guid, IReadOnlyCollection<Domain.Entities.BasketItem>> ChildrenByParent(
        IEnumerable<Domain.Entities.BasketItem> items) =>
        items.Where(i => i.ParentBasketItemId.HasValue)
            .GroupBy(i => i.ParentBasketItemId!.Value)
            .ToDictionary(g => g.Key, g => (IReadOnlyCollection<Domain.Entities.BasketItem>)g.ToList());

    private BasketLineCustomization? Customization(
        Domain.Entities.BasketItem row,
        Dictionary<Guid, IReadOnlyCollection<Domain.Entities.BasketItem>> childrenByParent) =>
        BasketLineCustomization.FromRow(
            row,
            childrenByParent.TryGetValue(row.Id, out var children)
                ? children
                : Array.Empty<Domain.Entities.BasketItem>(),
            (ex, what) => _logger.LogWarning(
                ex, "Failed to deserialize {What} JSON for basket item {BasketItemId}", what, row.Id));

    // Re-loads the user's basket with the full item graph (FindBasketAsync) and maps it.
    private async Task<BasketDto?> MapByUserAsync(Guid userId)
    {
        var basket = await _basketRepository.FindBasketAsync(null, userId);
        return basket != null ? await _basketMappingService.MapAsync(basket) : null;
    }
}
