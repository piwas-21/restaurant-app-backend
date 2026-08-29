using RestaurantSystem.Domain.Common.Base;

namespace RestaurantSystem.Domain.Entities;

/// <summary>
/// Restaurant identity + contact details. Singleton table — exactly one row
/// is expected; seeded by migration from the current i18n fallback values
/// so the deploy is non-breaking. Replaces hardcoded i18n keys
/// (rumi_address_*, rumi_phone_number) with admin-editable data.
/// </summary>
public class RestaurantInfo : Entity
{
    public required string Name { get; set; }
    public required string AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public required string City { get; set; }
    public required string PostalCode { get; set; }
    public required string Country { get; set; }
    public decimal? Latitude { get; set; }
    public decimal? Longitude { get; set; }
    public required string Email { get; set; }
    public string? Website { get; set; }

    /// <summary>
    /// Optional runtime colour-palette key (ADR-007). Null = the template's
    /// baked palette. Stored opaquely — the frontend owns the preset catalogue
    /// and safe-falls-back on an unknown key, so the backend does not validate
    /// it against a fixed list.
    /// </summary>
    public string? ThemePaletteKey { get; set; }

    /// <summary>
    /// The restaurant's own logo, uploaded through admin (SOFRA-ONBOARDING-PLAN O6).
    /// Null means the tenant has not uploaded one and the app renders its NAME as text —
    /// not a stand-in image. That fallback is the point of the field: before it existed
    /// every tenant image shipped with tenant-1's baked <c>/branding/logo.png</c>, so a
    /// new restaurant's header showed another restaurant's brand.
    /// </summary>
    public string? LogoUrl { get; set; }

    /// <summary>
    /// Optional dark-theme variant. Null falls back to <see cref="LogoUrl"/> — one logo
    /// that reads on both themes is the common case, and demanding two uploads to get a
    /// header at all would be worse than a slightly low-contrast mark.
    /// </summary>
    public string? LogoDarkUrl { get; set; }

    /// <summary>
    /// One photo of the restaurant itself — the dining room, the counter, the shopfront —
    /// uploaded through admin and rendered as a section on the landing page.
    /// Null means the tenant has not uploaded one and the section is NOT rendered at all.
    /// </summary>
    /// <remarks>
    /// The null case deliberately renders nothing rather than falling back to
    /// <c>/branding/hero.png</c>: that asset is a neutral platform graphic that belongs to no
    /// restaurant, so showing it under a heading like "our restaurant" would state something
    /// untrue about the tenant. Owned only by its own upload/delete endpoints, exactly like
    /// <see cref="LogoUrl"/> — the profile PUT never assigns it, so a client that PUTs the
    /// address without knowing this field exists cannot clear the photo.
    /// </remarks>
    public string? InteriorImageUrl { get; set; }

    public virtual ICollection<RestaurantPhoneNumber> PhoneNumbers { get; set; } = new List<RestaurantPhoneNumber>();
}
