using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.TableGuestVisits.Services.CustomerRounds;

public sealed class CustomerTableRoundDelivery(IOrderPostCreationService notifications,
    ILogger<CustomerTableRoundDelivery> logger) : ICustomerTableRoundDelivery
{
    public async Task NotifyAsync(Order order, Guid operationId, CancellationToken cancellationToken)
    {
        string? failureType = null;
        try { await notifications.CompleteAsync(order, false, cancellationToken); }
        catch (Exception exception)
        {
            // Provider messages and stacks may contain private data; retain only the failure class.
            failureType = exception.GetType().Name;
        }
        if (failureType is not null)
            logger.LogWarning("Post-commit table round notification failed for operation {OperationId} ({ExceptionType})",
                operationId, failureType);
    }
}
