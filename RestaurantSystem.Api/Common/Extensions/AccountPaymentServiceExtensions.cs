using RestaurantSystem.Api.Features.AccountPayments.Services;

namespace RestaurantSystem.Api.Common.Extensions;

public static class AccountPaymentServiceExtensions
{
    public static IServiceCollection AddAccountPaymentServices(this IServiceCollection services)
    {
        services.AddScoped<IAccountPaymentActorResolver, AccountPaymentActorResolver>();
        services.AddScoped<IAccountPaymentQuoteService, AccountPaymentQuoteService>();
        services.AddScoped<IAccountEqualSharePlanService, AccountEqualSharePlanService>();
        services.AddScoped<IAccountPaymentReservationService, AccountPaymentReservationService>();
        services.AddScoped<IAccountPaymentOperationReader, AccountPaymentOperationReader>();
        services.AddScoped<IAccountPaymentAccountReader, AccountPaymentAccountReader>();
        services.AddScoped<IAccountPaymentCaptureWriter, AccountPaymentCaptureWriter>();
        services.AddScoped<IAccountPaymentCaptureService, AccountPaymentCaptureService>();
        services.AddScoped<RestaurantSystem.Api.Features.OrderAmendments.Services.IOrderAmendmentReservationGuard,
            AccountPaymentOrderAmendmentReservationGuard>();
        return services;
    }
}
