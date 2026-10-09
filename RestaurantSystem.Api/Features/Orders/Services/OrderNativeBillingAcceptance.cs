using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.Orders.Services;

internal sealed class OrderNativeBillingAcceptance : IOrderNativeBillingAcceptance
{
    private readonly IOrderFidelityCoordinator _fidelity;
    private readonly IOrderBillingSnapshotWriter _snapshots;
    private readonly IOrderBillingAwardSuppressionWriter _awardSuppressions;

    public OrderNativeBillingAcceptance(
        IOrderFidelityCoordinator fidelity,
        IOrderBillingSnapshotWriter snapshots,
        IOrderBillingAwardSuppressionWriter awardSuppressions)
    {
        _fidelity = fidelity;
        _snapshots = snapshots;
        _awardSuppressions = awardSuppressions;
    }

    public Task<OrderBillingEarningEvaluation?> CalculatePointsToEarnAsync(
        Order order, decimal itemsTotal, Guid? userId, CancellationToken cancellationToken) =>
        _fidelity.CalculatePointsToEarnAsync(order, itemsTotal, userId, cancellationToken);

    public Task PreviewRedemptionAsync(
        Order order, int? pointsToRedeem, Guid? userId, CancellationToken cancellationToken) =>
        _fidelity.PreviewRedemptionAsync(order, pointsToRedeem, userId, cancellationToken);

    public Task<OrderBillingRedemptionEvidence?> RedeemAsync(
        Order order, int? pointsToRedeem, Guid? userId, CancellationToken cancellationToken,
        bool failOnError = false) =>
        _fidelity.RedeemAsync(order, pointsToRedeem, userId, cancellationToken, failOnError);

    public Task AwardEarnedPointsAsync(
        Order order, CancellationToken cancellationToken) =>
        _fidelity.AwardEarnedPointsAsync(order, cancellationToken);

    public Task WriteAcceptedSnapshotAsync(
        Order order,
        string? acceptedCurrency,
        OrderBillingEarningEvaluation? earning,
        OrderBillingRedemptionEvidence? redemption,
        CancellationToken cancellationToken) =>
        _snapshots.WriteAsync(order, acceptedCurrency, earning, redemption, cancellationToken);

    public Task RecordRemovedEarningUnitsAsync(
        Guid orderId,
        Guid amendmentId,
        CancellationToken cancellationToken) =>
        _awardSuppressions.RecordRemovedUnitsAsync(orderId, amendmentId, cancellationToken);
}
