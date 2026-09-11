using RestaurantSystem.Api.Features.Orders.Services;

namespace RestaurantSystem.Api.Common.Extensions;

public static class OrderPaymentServiceExtensions
{
    public static IServiceCollection AddOrderPaymentServices(this IServiceCollection services)
    {
        services.AddScoped<IOrderPaymentApplicator, OrderPaymentApplicator>();
        services.AddScoped<IOrderPaymentReplayResolver, OrderPaymentReplayResolver>();
        services.AddScoped<ITableBillAssembler, TableBillAssembler>();
        services.AddScoped<ITableBillPaymentOperationReplayResolver, TableBillPaymentOperationReplayResolver>();
        return services;
    }
}
