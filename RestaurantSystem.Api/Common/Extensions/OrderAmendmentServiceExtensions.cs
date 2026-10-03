using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using RestaurantSystem.Api.Features.OrderAmendments.Services;

namespace RestaurantSystem.Api.Common.Extensions;

public static class OrderAmendmentServiceExtensions
{
    public static IServiceCollection AddOrderAmendmentServices(this IServiceCollection services)
    {
        services.AddScoped<IOrderAmendmentFinancialResolution, OrderAmendmentFinancialResolutionService>();
        services.AddScoped<IOrderBillingAdjustmentWriter, OrderBillingAdjustmentWriter>();
        services.AddScoped<IOrderAmendmentQuoteService, OrderAmendmentQuoteService>();
        services.AddScoped<IOrderAmendmentCommitService, OrderAmendmentCommitService>();
        services.AddScoped<IOrderAmendmentQueryService, OrderAmendmentQueryService>();
        services.AddScoped<OrderAmendmentSupplementBuilder>();
        services.AddScoped<OrderAmendmentChangeBuilder>();
        services.AddScoped<OrderAmendmentCommitMaterializer>();
        services.AddScoped<OrderAmendmentCommitWriter>();
        services.AddScoped<OrderAmendmentKitchenStager>();
        services.TryAddScoped<IOrderAmendmentReservationGuard, UnavailableOrderAmendmentReservationGuard>();
        return services;
    }
}
