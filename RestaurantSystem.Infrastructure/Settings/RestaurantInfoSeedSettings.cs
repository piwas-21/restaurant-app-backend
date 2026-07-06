namespace RestaurantSystem.Infrastructure.Settings;

/// <summary>
/// First-boot tenant identity for the <c>RestaurantInfo</c> singleton (bound
/// from the "RestaurantInfoSeed" section; env vars override JSON, so per-tenant
/// provisioning injects RestaurantInfoSeed__Name / __City / __Email from the
/// tenant registry — sofra ADR-003, issue #120). When Name/Email are empty the
/// seeder is a no-op and the migration-seeded defaults stay in place — the safe
/// default for the legacy RUMI install.
/// </summary>
public class RestaurantInfoSeedSettings
{
    /// <summary>Tenant display name (registry <c>name</c>).</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Tenant city (registry <c>city</c>; optional).</summary>
    public string City { get; set; } = string.Empty;

    /// <summary>Tenant contact email (registry <c>admin_email</c>).</summary>
    public string Email { get; set; } = string.Empty;
}
