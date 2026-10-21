using RestaurantSystem.Api.Features.Orders.Dtos;

namespace RestaurantSystem.Api.Features.Orders.Services;

public interface ICurrentStaffTicketReader
{
    Task<IReadOnlyList<OrderDto>> ReadAsync(Guid orderId, CancellationToken cancellationToken);
}
