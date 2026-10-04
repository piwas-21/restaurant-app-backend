using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.Orders.Services;

public interface IOrderBillingSnapshotWriter
{
    Task WriteAsync(
        Order order,
        string? acceptedCurrency,
        OrderBillingEarningEvaluation? earning,
        OrderBillingRedemptionEvidence? redemption,
        CancellationToken cancellationToken);
}
