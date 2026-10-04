using RestaurantSystem.Api.Features.FidelityPoints.Interfaces;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.Orders.Services;

internal sealed class OrderNativeFidelityOperations : IOrderNativeFidelityOperations
{
    private readonly IFidelityPointsService _points;
    private readonly IOrderBillingEarningEvaluator _earningEvaluator;

    public OrderNativeFidelityOperations(
        IFidelityPointsService points,
        IOrderBillingEarningEvaluator earningEvaluator)
    {
        _points = points;
        _earningEvaluator = earningEvaluator;
    }

    public Task<OrderBillingEarningEvaluation> EvaluateOrderAsync(
        decimal rawRootTotal, CancellationToken cancellationToken) =>
        _earningEvaluator.EvaluateAsync(rawRootTotal, cancellationToken);

    public Task<FidelityPointBalance?> GetUserBalanceAsync(
        Guid userId, CancellationToken cancellationToken) =>
        _points.GetUserBalanceAsync(userId, cancellationToken);

    public Task<(FidelityPointsTransaction Transaction, decimal DiscountAmount)> RedeemPointsAsync(
        Guid userId, Guid orderId, int pointsToRedeem, CancellationToken cancellationToken) =>
        _points.RedeemPointsAsync(userId, orderId, pointsToRedeem, cancellationToken);

    public Task<FidelityPointsTransaction> AwardPointsAsync(
        Guid userId, Guid orderId, int points, decimal orderTotal, CancellationToken cancellationToken) =>
        _points.AwardPointsAsync(userId, orderId, points, orderTotal, cancellationToken);

    public decimal CalculateDiscountFromPoints(int points) =>
        _points.CalculateDiscountFromPoints(points);
}
