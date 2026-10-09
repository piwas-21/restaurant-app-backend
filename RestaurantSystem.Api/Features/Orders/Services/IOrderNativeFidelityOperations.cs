using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Api.Features.FidelityPoints.Models;

namespace RestaurantSystem.Api.Features.Orders.Services;

/// <summary>One order-scoped boundary for earning evidence and existing fidelity mutations.</summary>
public interface IOrderNativeFidelityOperations
{
    Task<OrderBillingEarningEvaluation> EvaluateOrderAsync(
        decimal rawRootTotal, CancellationToken cancellationToken);

    Task<FidelityPointBalance?> GetUserBalanceAsync(
        Guid userId, CancellationToken cancellationToken);

    Task<(FidelityPointsTransaction Transaction, decimal DiscountAmount)> RedeemPointsAsync(
        Guid userId, Guid orderId, int pointsToRedeem, CancellationToken cancellationToken);

    Task<FidelityPointsAwardResult> AwardAcceptedOrderAsync(
        Guid orderId, CancellationToken cancellationToken);

    decimal CalculateDiscountFromPoints(int points);
}
