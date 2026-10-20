namespace RestaurantSystem.Api.Features.Orders.Services.CustomerCancellation;

public interface ICustomerOrderCancellationNotifier
{
    Task NotifyAsync(Guid orderId, CancellationToken cancellationToken);
}
