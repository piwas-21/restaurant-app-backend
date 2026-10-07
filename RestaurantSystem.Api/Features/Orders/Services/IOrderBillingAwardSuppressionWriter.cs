namespace RestaurantSystem.Api.Features.Orders.Services;

public interface IOrderBillingAwardSuppressionWriter
{
    Task RecordRemovedUnitsAsync(
        Guid orderId,
        Guid amendmentId,
        CancellationToken cancellationToken);
}
