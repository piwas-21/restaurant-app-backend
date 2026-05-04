using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.Orders.Services;

/// <summary>
/// Coordinates the three fidelity-points side-effects of order creation:
///
/// 1. <see cref="CalculatePointsToEarnAsync"/> — pre-save: compute the
///    earnable points and stash on <c>Order.FidelityPointsEarned</c>.
///    Failures bubble (this is a pure calculation; if it can't run, the
///    order shouldn't ship with a half-set state).
/// 2. <see cref="RedeemAsync"/> — post-save: if the customer asked to
///    redeem points (<c>command.PointsToRedeem</c>), invoke the
///    redemption service, persist the updated <c>FidelityPointsRedeemed</c>
///    + <c>FidelityPointsDiscount</c>. Best-effort: failures logged, never
///    thrown — the customer can contact support.
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

    /// <summary>Post-save redemption (best-effort, persists via <c>SaveChangesAsync</c>).</summary>
    Task RedeemAsync(Order order, int? pointsToRedeem, Guid? userId, CancellationToken cancellationToken);

    /// <summary>Post-save award if payment is Completed/Overpaid (best-effort).</summary>
    Task AwardEarnedPointsAsync(Order order, Guid? userId, CancellationToken cancellationToken);
}
