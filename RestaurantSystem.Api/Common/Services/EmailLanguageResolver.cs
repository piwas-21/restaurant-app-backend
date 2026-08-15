using System.Globalization;
using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Domain.Common;
using RestaurantSystem.Infrastructure.Settings;

namespace RestaurantSystem.Api.Common.Services;

/// <summary>
/// The EMAIL-LOCALISATION-PLAN §1 chain over <see cref="LocalizationSettings"/> and the current
/// request's <c>Accept-Language</c>. See <see cref="IEmailLanguageResolver"/> for the contract.
/// </summary>
/// <remarks>
/// Singleton, like <c>ITenantModules</c>: the configured set is fixed for the process lifetime and
/// changes only through a re-provision plus restart. The per-request part comes from
/// <see cref="IHttpContextAccessor"/>, which is itself safe to hold in a singleton.
/// </remarks>
public sealed class EmailLanguageResolver : IEmailLanguageResolver
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public EmailLanguageResolver(
        IOptions<LocalizationSettings> options,
        IHttpContextAccessor httpContextAccessor,
        ILogger<EmailLanguageResolver> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _httpContextAccessor = httpContextAccessor ?? throw new ArgumentNullException(nameof(httpContextAccessor));

        var settings = options.Value;

        var configured = (settings.SupportedLanguages ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(LanguageCode.Normalize)
            .OfType<string>()
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        // Empty is NOT "no languages" — it is "unconfigured", which every instance is until the
        // deploy-side mapping ships and which the legacy RUMI install will always be, because it
        // runs the main compose project rather than a per-tenant one. Reading it as an empty
        // allow-list would leave no language to write a mail in at all.
        SupportedLanguages = configured.Length > 0 ? configured : LanguageCode.Supported;

        // A configured default outranks the list order; otherwise the first entry of the tenant's
        // OWN list, which is what §1 rank 4 means by "the tenant's language" — mailing a
        // French-only tenant its own alerts in English is the failure this avoids. The distinction
        // between "no list" and "a list" is load-bearing: SupportedLanguages[0] on an unconfigured
        // instance is `ar`, the first of the product's ten in alphabetical order, which would give
        // every legacy install Arabic operator alerts.
        TenantDefault = Supported(LanguageCode.Normalize(settings.DefaultLanguage))
            ?? (configured.Length > 0 ? configured[0] : LanguageCode.Fallback);

        if (configured.Length == 0 && !string.IsNullOrWhiteSpace(settings.SupportedLanguages))
        {
            // Reaching here means the key was set to something with no usable code in it. Warn
            // rather than throw: a typo in one tenant's .env must not stop that tenant booting.
            logger.LogWarning(
                "Localization:SupportedLanguages held no supported language code; using all {Count}.",
                SupportedLanguages.Count);
        }

        if (!string.IsNullOrWhiteSpace(settings.DefaultLanguage)
            && !string.Equals(TenantDefault, LanguageCode.Normalize(settings.DefaultLanguage), StringComparison.Ordinal))
        {
            logger.LogWarning(
                "Localization:DefaultLanguage '{Configured}' is not one this tenant supports; using {Effective}.",
                settings.DefaultLanguage, TenantDefault);
        }

        // The effective answer, once, at Information: the misconfiguration that produces no warning
        // at all is an unmapped key, which leaves this instance indistinguishable from an
        // unconfigured one. This line is what a container log can be asked, and what S9's smoke
        // test can assert on.
        logger.LogInformation(
            "Email languages: {Languages}; default {Default}.",
            string.Join(",", SupportedLanguages), TenantDefault);
    }

    public IReadOnlyList<string> SupportedLanguages { get; }

    public string TenantDefault { get; }

    public string Resolve(string? entityLanguage, string? userLanguage, string? requestLanguage) =>
        Supported(LanguageCode.Normalize(entityLanguage))    // rank 1
        ?? Supported(LanguageCode.Normalize(userLanguage))   // rank 2
        ?? Supported(LanguageCode.Normalize(requestLanguage))// rank 3 — passed in, never ambient
        ?? TenantDefault;                                    // rank 4 (rank 5 via TenantDefault)

    public string? FromRequest()
    {
        var header = _httpContextAccessor.HttpContext?.Request.Headers.AcceptLanguage;

        return header is null ? null : BestMatch(header.Value.ToString(), SupportedLanguages);
    }

    /// <summary>
    /// The highest-quality language in an <c>Accept-Language</c> header that this tenant sells in,
    /// or null. Parsed here rather than through typed headers so the whole rule is one testable
    /// function with no <c>HttpContext</c> in sight.
    /// </summary>
    /// <remarks>
    /// Three details a naive split gets wrong: <c>q=0</c> means "explicitly NOT this language" and
    /// must be skipped rather than ranked last; the wildcard <c>*</c> is not a language and must
    /// not select the first supported one; and a malformed quality is dropped rather than throwing,
    /// because this header is attacker-controlled on every anonymous endpoint the guest mails come
    /// from.
    /// </remarks>
    internal static string? BestMatch(string? header, IReadOnlyList<string> supported)
    {
        if (string.IsNullOrWhiteSpace(header))
        {
            return null;
        }

        return header
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(ParseEntry)
            .Where(entry => entry.Quality > 0)
            .OrderByDescending(entry => entry.Quality)
            .Select(entry => entry.Language)
            .OfType<string>()
            .FirstOrDefault(supported.Contains);
    }

    private static (string? Language, double Quality) ParseEntry(string entry)
    {
        var parts = entry.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var quality = 1d;

        foreach (var parameter in parts.Skip(1))
        {
            if (!parameter.StartsWith("q=", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // Invariant on purpose: the header's grammar says "0.8", never "0,8", whatever the
            // server's culture happens to be.
            // RFC 7231 bounds q to [0,1]. Anything else — "Infinity", "1e9", "-0.5", a word — is
            // a malformed entry, not a ranking: dropped, never thrown.
            quality = double.TryParse(
                parameter.AsSpan(2), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
                && parsed is > 0 and <= 1
                ? parsed
                : 0d;
        }

        return (LanguageCode.Normalize(parts.Length > 0 ? parts[0] : null), quality);
    }

    private string? Supported(string? language) =>
        language is not null && SupportedLanguages.Contains(language) ? language : null;
}
