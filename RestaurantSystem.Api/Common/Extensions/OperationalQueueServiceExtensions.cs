using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Api.Settings;

namespace RestaurantSystem.Api.Common.Extensions;

public static class OperationalQueueServiceExtensions
{
    public static IServiceCollection AddOperationalQueueSync(this IServiceCollection services)
    {
        services.AddOptions<OperationalQueueSyncOptions>()
            .BindConfiguration(OperationalQueueSyncOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddSingleton<IOperationalQueueCursor, OperationalQueueCursor>();
        services.AddScoped<IOperationalQueueSyncReader, OperationalQueueSyncReader>();
        return services;
    }
}
