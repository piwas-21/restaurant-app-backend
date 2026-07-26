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

    public virtual ICollection<RestaurantPhoneNumber> PhoneNumbers { get; set; } = new List<RestaurantPhoneNumber>();
}
