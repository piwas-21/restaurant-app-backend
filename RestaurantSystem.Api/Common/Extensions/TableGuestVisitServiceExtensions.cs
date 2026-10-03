using RestaurantSystem.Api.Features.TableGuestVisits.Services;

namespace RestaurantSystem.Api.Common.Extensions;

public static class TableGuestVisitServiceExtensions
{
    public static IServiceCollection AddTableGuestVisitServices(this IServiceCollection services)
    {
        services.AddScoped<ITableGuestAdmissionService, TableGuestAdmissionService>();
        services.AddScoped<ITableGuestAccountReader, TableGuestAccountReader>();
        services.AddScoped<ITableGuestParticipantPaymentAuthorization, TableGuestParticipantPaymentAuthorization>();
        services.AddScoped<ITableGuestVisitRevoker, TableGuestVisitRevoker>();
        services.AddScoped<ITableGuestRoundOperationStore, TableGuestRoundOperationStore>();
        return services;
    }
}
