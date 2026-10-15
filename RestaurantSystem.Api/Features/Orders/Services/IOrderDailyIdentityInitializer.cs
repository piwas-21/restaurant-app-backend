namespace RestaurantSystem.Api.Features.Orders.Services;

public interface IOrderDailyIdentityInitializer
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
}
