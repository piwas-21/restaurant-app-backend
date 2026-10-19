using RestaurantSystem.Api.Features.Payments.Services;

namespace RestaurantSystem.Api.Settings;

/// <summary>Reviewed central-link configuration; absent settings keep native creation unavailable.</summary>
public sealed class CustomerHostedCheckoutSettings
{
    public const string SectionName = "CustomerHostedCheckout";
    public bool Enabled { get; set; }
    public string LinkOrigin { get; set; } = string.Empty;
    public string TenantSlug { get; set; } = string.Empty;
    public bool IsValid() => !Enabled || NativeCheckoutRouting.IsValidConfiguration(LinkOrigin, TenantSlug);
}
