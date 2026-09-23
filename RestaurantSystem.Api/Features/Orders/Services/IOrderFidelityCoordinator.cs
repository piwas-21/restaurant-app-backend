using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.Orders.Services;

/// <summary>
/// Coordinates the three fidelity-points side-effects of order creation:
///
/// 1. <see cref="CalculatePointsToEarnAsync"/> — pre-save: compute the
///    earnable points and stash on <c>Order.FidelityPointsEarned</c>.
///    Failures bubble (this is a pure calculation; if it can't run, the
///    order shouldn't ship with a half-set state).
/// 2. <see cref="PreviewRedemptionAsync"/> — read-only quote calculation;
///    <see cref="RedeemAsync"/> writes the debit after order persistence.
///    Staff callers make this strict inside their operation transaction;
///    guest checkout retains best-effort behavior.
/// 3. <see cref="AwardEarnedPointsAsync"/> — post-save: if the order has
///    earnable points AND the payment is settled, award them. Cash payments
///    that stay Pending defer awarding to payment-completion time.
///    Best-effort: failures logged, never thrown.
///
/// Extracted from <c>CreateOrderCommandHandler</c> in Sprint 2 task 2.11.
/// </summary>
public interface IOrderFidelityCoordinator
{
    /// <summary>Pre-save calculation (sets <c>Order.FidelityPointsEarned</c>).</summary>
    Task CalculatePointsToEarnAsync(Order order, decimal itemsTotal, Guid? userId, CancellationToken cancellationToken);

    /// <summary>
    /// Read-only redemption preview. It updates the transient quote aggregate but never writes a
    /// points ledger row or balance.
    /// </summary>
    Task PreviewRedemptionAsync(
        Order order, int? pointsToRedeem, Guid? userId, CancellationToken cancellationToken);

    /// <summary>
    /// Post-save redemption. Staff creation requests strict failure so the ambient order
    /// transaction rolls back when the balance cannot be redeemed; guest checkout retains the
    /// historical best-effort behavior.
    /// </summary>
    Task RedeemAsync(
        Order order, int? pointsToRedeem, Guid? userId, CancellationToken cancellationToken,
        bool failOnError = false);

    /// <summary>Post-save award if payment is Completed/Overpaid (best-effort).</summary>
    Task AwardEarnedPointsAsync(Order order, Guid? userId, CancellationToken cancellationToken);
}
