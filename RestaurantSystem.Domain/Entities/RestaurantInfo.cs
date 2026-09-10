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
    /// Optional uploaded restaurant photo. Landing configuration uses it only when
    /// <see cref="LandingBackgroundMode"/> is <see cref="LandingBackgroundMode.Custom"/>.
    /// </summary>
    /// <remarks>
    /// Owned only by its upload/delete endpoints, exactly like <see cref="LogoUrl"/>. The profile
    /// PUT never assigns it, so an older client saving the address cannot remove the custom image.
    /// When absent, custom background mode is invalid; default and none remain valid states.
    /// </remarks>
    public string? InteriorImageUrl { get; set; }

    /// <summary>How the landing page resolves its background image.</summary>
    public LandingBackgroundMode LandingBackgroundMode { get; set; } = LandingBackgroundMode.Default;

    /// <summary>
    /// How the public menu page presents the catalogue: one tab per category
    /// (the default) or every category on one scrolling page. Stored as a
    /// string; the admin UI owns the labels, the guest site owns the rendering.
    /// </summary>
    public MenuLayout MenuLayout { get; set; } = MenuLayout.Tabs;

    /// <summary>
    /// Whether the guest "All items" tab also lists the tenant's menu bundles
    /// (partner request, mcdoner). Default false keeps every existing tenant's
    /// All tab products-only, which is the behaviour the platform shipped with.
    /// </summary>
    public bool ShowMenuBundlesOnAllTab { get; set; }

    /// <summary>
    /// The tenant's declared display currency (ISO-4217 alpha-3, e.g. "CHF", "EUR"),
    /// edited by the admin through restaurant-info settings. Null = the tenant has not
    /// declared one — order surfaces then fall back to the tender's own currency and
    /// must not invent a label (POS plan C18: receipts used to hardcode CHF while a
    /// tenant traded in EUR). Display metadata only; pricing and rounding are untouched.
    /// </summary>
    public string? Currency { get; set; }

    public virtual ICollection<RestaurantPhoneNumber> PhoneNumbers { get; set; } = new List<RestaurantPhoneNumber>();
    public virtual ICollection<RestaurantLandingContent> LandingContents { get; set; } = new List<RestaurantLandingContent>();
}

/// <summary>How the public menu page presents the catalogue.</summary>
public enum MenuLayout
{
    /// <summary>The current behaviour: All items, Menu Bundles, then one tab per category.</summary>
    Tabs,

    /// <summary>Every category on one page; the category bar scrolls to each section.</summary>
    OnePage,
}
