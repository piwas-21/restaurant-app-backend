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
    private const string DefaultCurrency = "CHF";
    private string _currency = DefaultCurrency;

    /// <summary>
    /// Tenant display currency label (registry <c>currency</c>). Falls back to
    /// <c>CHF</c> when unset or blank, so an empty <c>Localization__Currency</c>
    /// (e.g. a tenant provisioned with an empty <c>TENANT_CURRENCY</c>) never
    /// renders a blank currency in emails.
    /// </summary>
    public string Currency
    {
        get => string.IsNullOrWhiteSpace(_currency) ? DefaultCurrency : _currency;
        set => _currency = value;
    }
}
