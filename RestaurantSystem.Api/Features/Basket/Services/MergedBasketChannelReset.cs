using System.Text.Json;
using RestaurantSystem.Api.Features.Catalog;
using RestaurantSystem.Domain.Common;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Basket.Services;

/// <summary>
/// The channel half of the anonymous-basket merge (ORDER-TYPE-AVAILABILITY-PLAN G11), extracted from
/// <see cref="AnonymousBasketMerger"/> when #313 pushed that class past the §4 service limit. It is a
/// separate concern with one output — whether the surviving basket keeps its order type — and it sits
/// beside its collaborators <c>BasketLineChannelScan</c> and <c>BasketChannelGuard</c> rather than
/// inside the merge loop. Behaviour is unchanged; only its home is.
///
/// #313 made it matter more: rows that used to be merged and hard-deleted now re-home when their
/// customization differs, so more lines reach this scan than before.
/// </summary>
public static class MergedBasketChannelReset
{
    /// <summary>
    /// Merging re-homes rows by direct assignment, so it bypasses the add-to-basket channel guard —
    /// a Takeaway anonymous basket merging into a DineIn user basket could land lines that are
    /// invalid for the surviving channel (ORDER-TYPE-AVAILABILITY-PLAN G11).
    /// </summary>
    /// <remarks>
    /// The rule: never drop a line (the guest chose it) and never keep an invalid line under a
    /// channel. So on conflict the CHANNEL is cleared instead — the guest re-picks, and the normal
    /// two-phase switch (IBasketChannelService) then shows a proper itemized confirm. Clearing a
    /// channel is always safe: null is the permissive browse state.
    /// </remarks>
    public static async Task ApplyAsync(
        ApplicationDbContext context,
        ILogger logger,
        Domain.Entities.Basket userBasket,
        List<Domain.Entities.BasketItem> rehomed,
        IReadOnlyCollection<Domain.Entities.BasketItem> anonymousItems)
    {
        if (userBasket.OrderType is null || rehomed.Count == 0)
        {
            return;
        }

        // Inspect the EXPLICIT re-homed list, not userBasket.Items. The merge loop re-homes rows by
        // scalar FK assignment (item.BasketId = ...), and EF only fixes up the Items navigation
        // during DetectChanges/SaveChanges — which happens AFTER this runs. Reading
        // userBasket.Items here would see only the user's pre-existing lines, every one of which was
        // already validated by BasketChannelGuard when it was added, so the check would be a no-op
        // for exactly the scenario it exists to catch.
        //
        // §9.15: `rehomed` holds ROOTS only (the merge loop iterates roots and re-homes each one's
        // children alongside it), so the descendants are resolved from `anonymousItems` — the same
        // snapshot the loop walked. Scanning roots alone let a bundle whose COMPONENT the surviving
        // channel forbids merge in silently, which is this method's own failure mode one level down.
        List<string> conflicting;
        try
        {
            var productIds = BasketLineChannelScan.CollectProductIds(rehomed, anonymousItems);
            if (productIds.Count == 0)
            {
                return;
            }

            var products = await BasketLineChannelScan.LoadProductsAsync(context, productIds, CancellationToken.None);

            conflicting = rehomed
                .Where(root => !OrderChannelMap.Allows(
                    BasketLineChannelScan.LineMask(root, anonymousItems, products), userBasket.OrderType.Value))
                .Select(root => root.ProductId is { } id && products.TryGetValue(id, out var p) ? p.Name : "(unknown item)")
                .ToList();
        }
        catch (JsonException ex)
        {
            // Caught HERE and nowhere else. BasketLineChannelScan deliberately lets a corrupt
            // SelectedSideItemsJson throw, because a swallowed parse failure makes the scan silently
            // permissive — but on THIS path a throw is worse than the bug it guards. It would escape
            // before SaveChangesAsync, abandoning the adopt, the re-home AND the soft-delete, and
            // BasketMergeService catches everything out of here on purpose ("basket merge should not
            // break login flow") — so the guest would log in and watch their whole basket vanish,
            // silently. The switch path has no such swallow: there the throw surfaces as a 500 the
            // guest can retry, and the cart still renders.
            //
            // Clearing the channel is the fail-safe this method already has: it is what a genuine
            // conflict does, it never drops a line, and null is the permissive browse state. So an
            // unreadable line is treated as conflicting rather than as fine.
            logger.LogError(ex,
                "Unreadable side-item JSON while merging into basket {BasketId}; clearing order type {OrderType} "
                + "rather than assuming the merged lines are orderable",
                userBasket.Id, userBasket.OrderType);

            userBasket.OrderType = null;
            return;
        }

        if (conflicting.Count == 0)
        {
            return;
        }

        logger.LogInformation(
            "Cleared order type {OrderType} on merged basket {BasketId}: {Count} line(s) unavailable ({Names})",
            userBasket.OrderType, userBasket.Id, conflicting.Count, string.Join(", ", conflicting));

        userBasket.OrderType = null;
    }
}
