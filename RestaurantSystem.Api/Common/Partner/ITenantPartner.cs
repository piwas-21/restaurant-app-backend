namespace RestaurantSystem.Api.Common.Partner;

/// <summary>
/// The partner attribution this tenant instance publishes, already validated
/// (workspace docs/plans/SOFRA-PARTNER-PLAN.md §11, slice S4a).
///
/// Resolved once at startup from <see cref="Settings.PartnerSettings"/>, matching
/// <see cref="Modules.ITenantModules"/>: a change takes effect on the next backend restart,
/// which is exactly when a re-provision rewrites the tenant .env.
/// </summary>
public interface ITenantPartner
{
    /// <summary>
    /// The partner's brand name, or <c>null</c> when this tenant has no attribution to show —
    /// no partner, attribution switched off, or a tenant provisioned before the deploy-side
    /// mapping shipped. Consumers render nothing when this is null.
    /// </summary>
    string? Name { get; }

    /// <summary>
    /// The partner's website, or <c>null</c>. Null whenever <see cref="Name"/> is null (a link
    /// with no label is not attribution) and whenever the configured value is not an absolute
    /// https:// URI. A name without a url is a legitimate, renderable answer: plain text.
    /// </summary>
    string? Url { get; }
}
