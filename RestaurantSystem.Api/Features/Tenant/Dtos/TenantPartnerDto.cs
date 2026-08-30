namespace RestaurantSystem.Api.Features.Tenant.Dtos;

/// <summary>
/// The partner attribution this tenant publishes in its footer
/// (workspace docs/plans/SOFRA-PARTNER-PLAN.md §11, D-B1: attribution only — a brand name and
/// a link, never an address, a personal legal name or a phone number).
/// </summary>
/// <param name="Name">
/// The partner's brand name, or <c>null</c> when there is nothing to credit. Null is the
/// normal answer, not an error: it is every tenant with no partner, every tenant whose
/// restaurant switched attribution off, and every tenant provisioned before the deploy slice.
/// Clients render nothing when it is null.
/// </param>
/// <param name="Url">
/// The partner's website, or <c>null</c>. Always null when <paramref name="Name"/> is null,
/// and null on its own when the configured value is not an absolute https:// URL — so a
/// client may treat a non-null value as safe to put in an href (with rel="noopener noreferrer").
/// A non-null name with a null url renders as plain text.
/// </param>
public record TenantPartnerDto(string? Name, string? Url);
