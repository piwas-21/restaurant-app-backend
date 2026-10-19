using RestaurantSystem.Api.Features.Basket.Interfaces;

namespace RestaurantSystem.Api.Features.Basket.Services.Operations;

public static class CustomerBasketOperationRegistration
{
    public static IServiceCollection AddCustomerBasketOperations(this IServiceCollection services) =>
        services.AddScoped<ICustomerBasketOperationService, CustomerBasketOperationService>();
}
