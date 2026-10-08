using RestaurantSystem.Api.Features.KitchenBoard.Services;

namespace RestaurantSystem.Api.Common.Extensions;

public static class KitchenBoardServiceExtensions
{
    public static IServiceCollection AddKitchenBoardServices(this IServiceCollection services)
    {
        services.AddScoped<IKitchenBoardWorkReader, KitchenBoardWorkReader>();
        return services;
    }
}
