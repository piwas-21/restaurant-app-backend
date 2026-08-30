namespace RestaurantSystem.Api.Settings;

/// <summary>
/// The partner (reseller) who built and provisioned this tenant instance, as published in
/// the customer-facing footer (workspace docs/plans/SOFRA-PARTNER-PLAN.md §11, slice S4a).
///
/// Bound from the "Partner" configuration section. The deploy repo's tenant compose template
/// maps the tenant .env onto it — <c>Partner__Name: "${TENANT_PARTNER_NAME:-}"</c> and
/// <c>Partner__Url: "${TENANT_PARTNER_URL:-}"</c> — exactly as it already does for
/// <see cref="ModuleSettings"/>. Both defaults are empty, which means NO ATTRIBUTION, so an
/// instance nobody has configured (every instance today) behaves as it did before this existed.
///
/// There is deliberately no "attribution on/off" flag here: the registry's
/// <c>partner_attribution:</c> boolean is resolved in <c>provision-tenant.sh</c>, which writes
/// EMPTY values when it is off. So these two keys carry exactly one meaning — what to display.
/// </summary>
public class PartnerSettings
{
    /// <summary>Configuration section these keys are bound from.</summary>
    public const string SectionName = "Partner";

    /// <summary>
    /// The partner's public brand name, e.g. "Solution Eva". EMPTY MEANS NO ATTRIBUTION:
    /// the footer renders nothing, which is the state of every tenant provisioned before S3b.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// The partner's public website. Only served when it parses as an absolute https:// URI
    /// (see <c>TenantPartner</c>) — it becomes an href on a public page.
    /// </summary>
    public string Url { get; set; } = string.Empty;
}
