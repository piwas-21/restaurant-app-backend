using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Settings;

namespace RestaurantSystem.Api.Features.Tenant.Services;

/// <summary>Public native authentication flags share the current accounts capability prerequisite.</summary>
public sealed class CustomerNativeAuthenticationCapabilities(IOptions<CustomerNativeAccountSettings> accounts,
    IOptions<CustomerNativeSocialSettings> social) : ICustomerNativeAuthenticationCapabilities
{
    public CustomerNativeAuthenticationFlags Resolve(bool accountsEnabled) => new(
        accountsEnabled && accounts.Value.Enabled && accounts.Value.IsValid(),
        accountsEnabled && social.Value.GoogleEnabled && social.Value.IsValid());
}
