using RestaurantSystem.Api.Features.Orders.Services;

namespace RestaurantSystem.Api.Common.Extensions;

public static class OrderCreationServiceExtensions
{
    public static IServiceCollection AddOrderCreationServices(this IServiceCollection services)
    {
        services.AddScoped<IOrderCreationWriter, OrderCreationWriter>();
        services.AddScoped<IOrderPostCreationService, OrderPostCreationService>();
        return services;
    }
}
