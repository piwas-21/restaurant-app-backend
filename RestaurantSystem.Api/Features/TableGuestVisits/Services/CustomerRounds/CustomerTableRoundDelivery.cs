using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.TableGuestVisits.Services.CustomerRounds;

public sealed class CustomerTableRoundDelivery(IOrderPostCreationService notifications,
    ILogger<CustomerTableRoundDelivery> logger) : ICustomerTableRoundDelivery
{
    public async Task NotifyAsync(Order order, Guid operationId, CancellationToken cancellationToken)
    {
        try { await notifications.CompleteAsync(order, false, cancellationToken); }
        catch (Exception) { logger.LogWarning("Post-commit table round notification failed for operation {OperationId}", operationId); }
    }
}
