namespace RestaurantSystem.Api.Features.Tenant.Services;

/// <summary>Combines explicit native rollout policies without altering the base module/feature decisions.</summary>
public sealed class CustomerNativeCapabilities(ICustomerNativeAuthenticationCapabilities authentication,
    ICustomerNativeTablePaymentReadiness tablePayments) : ICustomerNativeCapabilities
{
    public CustomerNativeCapabilityResolution Resolve(IReadOnlyDictionary<string, string> capabilities, string? currency)
    {
        var resolved = capabilities.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        if (resolved.GetValueOrDefault("table-payments") == "unknown" && tablePayments.IsReady(currency))
            resolved["table-payments"] = "enabled";
        return new(resolved, authentication.Resolve(resolved.GetValueOrDefault("accounts") == "enabled"));
    }
}
