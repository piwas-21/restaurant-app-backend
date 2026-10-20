using RestaurantSystem.Api.Features.Orders.Services.CustomerCancellation;

namespace RestaurantSystem.Api.Common.Extensions;

public static class CustomerOrderCancellationServiceExtensions
{
    public static IServiceCollection AddCustomerOrderCancellationServices(this IServiceCollection services)
    {
        services.AddScoped<ICustomerOrderCancellationOwners, CustomerOrderCancellationOwners>();
        services.AddScoped<ICustomerOrderCancellationRules, CustomerOrderCancellationRules>();
        services.AddScoped<ICustomerOrderCancellationStore, CustomerOrderCancellationStore>();
        services.AddScoped<ICustomerOrderCancellationNotifier, CustomerOrderCancellationNotifier>();
        services.AddScoped<ICustomerOrderCancellationService, CustomerOrderCancellationService>();
        return services;
    }
}
