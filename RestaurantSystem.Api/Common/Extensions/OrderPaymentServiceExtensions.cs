using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Api.Features.TableServiceSessions.Services;

namespace RestaurantSystem.Api.Common.Extensions;

public static class OrderPaymentServiceExtensions
{
    public static IServiceCollection AddOrderPaymentServices(this IServiceCollection services)
    {
        services.AddScoped<IOrderPaymentApplicator, OrderPaymentApplicator>();
        services.AddScoped<IOrderPaymentReplayResolver, OrderPaymentReplayResolver>();
        services.AddScoped<ITableBillAssembler, TableBillAssembler>();
        services.AddScoped<ITableBillPaymentOperationReplayResolver, TableBillPaymentOperationReplayResolver>();
        services.AddScoped<ITableBillTargetResolver, TableBillTargetResolver>();
        services.AddScoped<ITableServiceSessionReader, TableServiceSessionReader>();
        services.AddScoped<ITableServiceSessionPaymentReplayResolver, TableServiceSessionPaymentReplayResolver>();
        services.AddScoped<ITableServiceSessionPaymentWriter, TableServiceSessionPaymentWriter>();
        services.AddScoped<ITableServiceSessionCurrencyPolicy, TableServiceSessionCurrencyPolicy>();
        return services;
    }
}
