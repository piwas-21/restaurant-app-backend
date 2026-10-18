using RestaurantSystem.Api.Features.Tenant.Services;

namespace RestaurantSystem.Api.Settings;

/// <summary>Explicit per-tenant native activation; absent configuration keeps the endpoint unavailable.</summary>
public sealed class CustomerMobileSettings
{
    public const string SectionName = "CustomerMobile";
    public bool Enabled { get; set; }
    public Guid TenantId { get; set; }
    public string Lifecycle { get; set; } = "disabled";
    public string[] EnabledCapabilities { get; set; } = [];

    public bool IsValid() => !Enabled || (TenantId != Guid.Empty
        && Lifecycle is "active" or "read-only" or "disabled"
        && EnabledCapabilities.All(MobileCapabilityPolicy.IsKnown));
}
