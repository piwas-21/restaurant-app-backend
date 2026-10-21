using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.TableGuestVisits.Services.CustomerRounds;

public interface ICustomerTableRoundDelivery
{
    Task NotifyAsync(Order order, Guid operationId, CancellationToken cancellationToken);
}
