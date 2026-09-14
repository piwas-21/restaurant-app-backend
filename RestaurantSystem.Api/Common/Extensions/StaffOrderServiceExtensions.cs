using RestaurantSystem.Api.Features.Orders.Services;

namespace RestaurantSystem.Api.Common.Extensions;

public static class StaffOrderServiceExtensions
{
    public static IServiceCollection AddStaffOrderServices(this IServiceCollection services)
    {
        services.AddScoped<IStaffCounterOrderBuilder, StaffCounterOrderBuilder>();
        services.AddScoped<IStaffCounterOrderPricing, StaffCounterOrderPricing>();
        services.AddScoped<IStaffOrderOperationStore, StaffOrderOperationStore>();
        return services;
    }
}
