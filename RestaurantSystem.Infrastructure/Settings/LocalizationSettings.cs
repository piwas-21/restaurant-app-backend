namespace RestaurantSystem.Infrastructure.Settings;

/// <summary>
/// Tenant display currency label (ISO-4217-ish, e.g. "CHF", "EUR") shown in
/// order-confirmation emails. Sourced per-tenant from the tenant registry
/// <c>currency</c> field -&gt; <c>TENANT_CURRENCY</c> container env var -&gt;
/// <c>Localization__Currency</c> (env vars override JSON; bound from the
/// "Localization" section). Default <c>CHF</c> is the legacy RUMI install.
/// </summary>
public class LocalizationSettings
{
    /// <summary>Tenant display currency label (registry <c>currency</c>).</summary>
    public string Currency { get; set; } = "CHF";
}
