using RestaurantSystem.Api.Features.Orders.Services.CustomerCheckout;

namespace RestaurantSystem.Api.Common.Extensions;

public static class CustomerCheckoutServiceExtensions
{
    public static IServiceCollection AddCustomerCheckoutServices(this IServiceCollection services)
    {
        services.AddScoped<ICustomerCheckoutGuard, CustomerCheckoutGuard>();
        services.AddScoped<ICustomerCheckoutItems, CustomerCheckoutItems>();
        services.AddScoped<ICustomerCheckoutDraft, CustomerCheckoutDraft>();
        services.AddScoped<ICustomerCheckoutQuoteService, CustomerCheckoutQuoteService>();
        services.AddScoped<ICustomerCheckoutIntentPolicy, CustomerCheckoutIntentPolicy>();
        services.AddScoped<ICustomerCheckoutAcceptance, CustomerCheckoutAcceptance>();
        return services;
    }
}
