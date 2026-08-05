using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.Basket.Services;

/// <summary>
/// Keeps a menu bundle's child rows in step when the parent line's quantity changes (#305).
///
/// A bundle child stores a LINE-ABSOLUTE count. <c>BasketItemFactory</c> builds it as
/// <c>Quantity = item.Quantity * option.Quantity</c> — the per-unit choice multiplied by the line
/// quantity — while its <c>UnitPrice</c> stays per-unit (the section's AdditionalPrice). That pairing
/// is what makes <c>child.Quantity * child.UnitPrice</c> the component's share of the line at add
/// time, and it is why the count cannot simply be left alone when the line quantity moves: the two
/// factors stop agreeing and the guest is shown a number that no longer reconciles.
///
/// Money is NOT affected either way — the parent's <c>UnitPrice</c> carries the whole line price and
/// is multiplied by the parent's own quantity. This is a displayed-count fix. That is also the trap:
/// every existing test passed, because the totals were always right.
/// </summary>
public static class BundleChildQuantityScaler
{
    /// <summary>
    /// Rescales <paramref name="children"/> from <paramref name="previousQuantity"/> to
    /// <paramref name="newQuantity"/>, preserving each child's per-unit count.
    /// </summary>
    /// <remarks>
    /// Callers must pass the children they have actually LOADED. This helper deliberately does no
    /// querying: an un-included <c>ChildBasketItems</c> navigation reads as an empty collection
    /// rather than throwing, so a missing Include would silently turn the whole fix into a no-op
    /// with every test still green. Keeping the load at the call site makes that omission visible
    /// there instead of hiding it behind a helper that looks like it handles loading.
    /// </remarks>
    public static void Rescale(
        IEnumerable<BasketItem> children,
        int previousQuantity,
        int newQuantity,
        string auditIdentifier)
    {
        ArgumentNullException.ThrowIfNull(children);

        // previousQuantity <= 0 would make the per-unit count unrecoverable (it is the divisor).
        // The validator pins 1..100, so this is unreachable defensively rather than expected.
        if (previousQuantity <= 0 || newQuantity == previousQuantity)
        {
            return;
        }

        foreach (var child in children)
        {
            // The row must be an exact multiple of the OLD line quantity, because that is what a
            // healthy child is: `child.Quantity == previousQuantity * option.Quantity`. When it is
            // not, the per-unit factor is simply not recoverable from this row, and every way of
            // guessing it makes the row worse than leaving it alone:
            //
            //   * an earlier draft clamped with `Math.Max(1, …)`. On a basket that was already
            //     live when this fix deployed — parent moved, children never rescaled — that turns
            //     a stale-but-proportional row into a flat 1 and destroys the per-unit factor for
            //     every later change. Measured: parent 50 → 2 with a per-unit-2 child rewrote 2 to
            //     1 instead of 4, permanently.
            //   * it also laundered an arithmetic overflow into a plausible number (see below).
            //
            // So skip. A stale row is the status quo this fix is improving on; a rewritten one is a
            // new wrong answer that looks deliberate.
            if (child.Quantity % previousQuantity != 0)
            {
                continue;
            }

            // 64-bit intermediate. An earlier comment here claimed `int` could not overflow because
            // quantities are validated to 1..100 — that is false, and was measured: the 1..100 rule
            // binds the LINE quantity only, `SelectedMenuOptions[].Quantity` has no upper bound at
            // all (BasketItemFactory rejects only < 1). An option quantity of 30,000,000 is accepted
            // today, and 30,000,000 * 100 wraps int32 negative. Widening here removes the wrap; the
            // missing input bound is a separate defect — issue #308.
            //
            // Multiply BEFORE dividing so the divisible case stays exact.
            var rescaled = (long)child.Quantity * newQuantity / previousQuantity;

            // Divisibility above guarantees child.Quantity / previousQuantity >= 1, so this cannot
            // produce 0 — no floor needed, and none is applied, because a floor here is exactly the
            // silent rewrite the skip above exists to avoid.
            child.Quantity = checked((int)rescaled);

            // ItemTotal is deliberately NOT recomputed. A child carries ItemTotal = 0 by design so
            // it cannot double-count against the parent during basket recalculation
            // (BasketItemFactory sets it to 0 explicitly). Recomputing it here — the obvious
            // instinct next to a quantity write — would inflate every bundle basket total.
            child.UpdatedAt = DateTime.UtcNow;
            child.UpdatedBy = auditIdentifier;
        }
    }
}
