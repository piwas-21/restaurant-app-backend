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

    /// <summary>
    /// Comma-separated language codes this tenant sells in (registry <c>languages</c> ->
    /// <c>TENANT_LANGUAGES</c> -> <c>Localization__SupportedLanguages</c>, wired in S9).
    /// EMPTY MEANS ALL TEN: the legacy RUMI install runs the main compose project and will
    /// never receive this key, exactly as with <c>Modules__Enabled</c>. Interpreted by
    /// <c>IEmailLanguageResolver</c>, which is the only reader.
    /// </summary>
    public string SupportedLanguages { get; set; } = string.Empty;

    /// <summary>
    /// Language for mail with no guest to follow — operator alerts, background jobs. Blank or
    /// unsupported falls back to the first entry of <see cref="SupportedLanguages"/>, then to
    /// <c>en</c>, so a tenant that lists only French never gets an English alert by omission.
    /// </summary>
    public string DefaultLanguage { get; set; } = string.Empty;
}
