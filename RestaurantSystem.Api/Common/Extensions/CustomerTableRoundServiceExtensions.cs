using RestaurantSystem.Api.Features.TableGuestVisits.Services.CustomerRounds;

namespace RestaurantSystem.Api.Common.Extensions;

public static class CustomerTableRoundServiceExtensions
{
    public static IServiceCollection AddCustomerTableRoundServices(this IServiceCollection services)
    {
        services.AddScoped<ICustomerTableRoundAccess, CustomerTableRoundAccess>();
        services.AddScoped<ICustomerTableRoundProofs, CustomerTableRoundProofs>();
        services.AddScoped<ICustomerTableRoundQuotes, CustomerTableRoundQuotes>();
        services.AddScoped<ICustomerTableRoundReceipts, CustomerTableRoundReceipts>();
        services.AddScoped<ICustomerTableRoundWriter, CustomerTableRoundWriter>();
        services.AddScoped<ICustomerTableRoundExecution, CustomerTableRoundExecution>();
        services.AddScoped<ICustomerTableRoundDelivery, CustomerTableRoundDelivery>();
        services.AddScoped<ICustomerTableRoundService, CustomerTableRoundService>();
        return services;
    }
}
