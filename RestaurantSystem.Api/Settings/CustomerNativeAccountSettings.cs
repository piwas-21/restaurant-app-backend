using RestaurantSystem.Api.Features.Payments.Services;

namespace RestaurantSystem.Api.Settings;

/// <summary>Separate opt-in for centrally routed native email actions; absent settings are inert.</summary>
public sealed class CustomerNativeAccountSettings
{
    public const string SectionName = "CustomerNativeAccounts";
    public bool Enabled { get; set; }
    public string LinkOrigin { get; set; } = string.Empty;
    public string TenantSlug { get; set; } = string.Empty;
    public int LinkLifetimeMinutes { get; set; } = 60;

    public bool IsValid() => !Enabled || LinkLifetimeMinutes is >= 5 and <= 120
        && NativeCheckoutRouting.IsValidConfiguration(LinkOrigin, TenantSlug);
}
