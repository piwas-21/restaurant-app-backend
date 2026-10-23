using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Features.AccountPayments.Services;
using RestaurantSystem.Api.Settings;

namespace RestaurantSystem.Api.Features.Tenant.Services;

/// <summary>Explicit native approval plus locally configured provider authority; no provider network probe.</summary>
public sealed class CustomerNativeTablePaymentReadiness(IOptions<CustomerHostedCheckoutSettings> hosted,
    IGuestAccountPaymentPolicy policy) : ICustomerNativeTablePaymentReadiness
{
    public bool IsReady(string? currency) => hosted.Value.TablePaymentsEnabled
        && hosted.Value.Enabled && hosted.Value.IsValid()
        && currency is { Length: 3 } && currency == currency.ToUpperInvariant()
        && policy.ReadLimits(currency) is not null;
}
