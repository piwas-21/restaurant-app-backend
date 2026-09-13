using RestaurantSystem.Api.Common.Services;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Api.Settings;

namespace RestaurantSystem.Api.Common.Extensions;

public static class OrderDetailServiceExtensions
{
    public static IServiceCollection AddOrderDetailServices(this IServiceCollection services)
    {
        services.AddOptions<OrderWorkflowSettings>()
            .BindConfiguration(OrderWorkflowSettings.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddScoped<IRetainedCustomerDataScrubber, RetainedCustomerDataScrubber>();
        services.AddScoped<IOrderPermittedActionsService, OrderPermittedActionsService>();
        services.AddScoped<IOrderResponseProjector, OrderResponseProjector>();
        return services;
    }
}
