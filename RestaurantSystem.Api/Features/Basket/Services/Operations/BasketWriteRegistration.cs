using RestaurantSystem.Api.Features.Basket.Interfaces;

namespace RestaurantSystem.Api.Features.Basket.Services.Operations;

public static class BasketWriteRegistration
{
    public static IServiceCollection AddBasketWriteCoordination(this IServiceCollection services)
    {
        services.AddScoped<IBasketWriteCoordinator, BasketWriteCoordinator>();
        services.AddScoped<BasketService>();
        services.AddScoped<BasketChannelService>();
        services.AddScoped<IBasketChannelConflictService, BasketChannelConflictService>();
        services.AddScoped<IBasketService, SerializedBasketService>();
        services.AddScoped<IBasketChannelService, SerializedBasketChannelService>();
        return services;
    }
}
