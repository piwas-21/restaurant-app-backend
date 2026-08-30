using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Settings;

namespace RestaurantSystem.Api.Common.Partner;

/// <summary>
/// Reads the tenant's partner attribution once at startup and normalises it into the two
/// values that are safe to publish.
///
/// The rules, in order:
///   1. no name configured        -> nothing at all (name AND url null)
///   2. name set, url unusable    -> the name alone, rendered as plain text
///   3. name set, url is https    -> both
///
/// Rule 1 is the load-bearing one: it is EVERY tenant today, and every tenant provisioned
/// before the deploy slice (S3b) that writes TENANT_PARTNER_NAME. Rule 2 is why the url is
/// dropped independently of the name — withholding a bad link must not also withhold a
/// correct credit.
/// </summary>
public sealed class TenantPartner : ITenantPartner
{
    public TenantPartner(IOptions<PartnerSettings> options, ILogger<TenantPartner> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        var settings = options.Value;

        var name = (settings.Name ?? string.Empty).Trim();
        Name = name.Length == 0 ? null : name;

        Url = Name is null ? null : SafePublicUrl((settings.Url ?? string.Empty).Trim(), logger);

        if (Name is not null)
        {
            logger.LogInformation(
                "Partner attribution ON — name: {PartnerName}, url published: {HasUrl}",
                Name, Url is not null);
        }
    }

    public string? Name { get; }

    public string? Url { get; }

    /// <summary>
    /// Accepts only an absolute https:// URI, because this value becomes an <c>href</c> on a
    /// page served to the public. Rejecting anything else here stops <c>javascript:</c>,
    /// <c>data:</c> and protocol-relative strings at the boundary, and rejects plain http so a
    /// credit link cannot downgrade the page.
    ///
    /// DEFENCE IN DEPTH, NOT A DUPLICATE: the deploy repo's provision-tenant.sh validates the
    /// same registry value before writing it (SOFRA-PARTNER-PLAN §11g). The two checks live in
    /// different repos and guard different entry points — a hand-edited .env on the box never
    /// passes through that script, and this process trusts nothing it did not parse itself.
    /// Do not delete either as redundant.
    /// </summary>
    private static string? SafePublicUrl(string candidate, ILogger logger)
    {
        if (candidate.Length == 0) return null;

        if (Uri.TryCreate(candidate, UriKind.Absolute, out var uri)
            && string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal))
        {
            return uri.AbsoluteUri;
        }

        // Warn rather than fail startup: a bad link must not take a live tenant down, and the
        // name still publishes. The value is operator-supplied, so it is safe to log.
        logger.LogWarning(
            "Ignoring Partner:Url {PartnerUrl} — only an absolute https:// URL is published", candidate);
        return null;
    }
}
