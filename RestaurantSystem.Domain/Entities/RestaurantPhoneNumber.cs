using RestaurantSystem.Domain.Common.Base;

namespace RestaurantSystem.Domain.Entities;

/// <summary>
/// One of N phone numbers attached to <see cref="RestaurantInfo"/>. Number is
/// stored in E.164 format (e.g. <c>+41227863333</c>); UI is responsible for
/// pretty-printing.
/// </summary>
public class RestaurantPhoneNumber : Entity
{
    public Guid RestaurantInfoId { get; set; }

    /// <summary>
    /// Free-form label shown in admin UI / footer (e.g. "Reservations",
    /// "Reception"). Optional.
    /// </summary>
    public string? Label { get; set; }

    /// <summary>E.164 phone number, e.g. <c>+41227863333</c>.</summary>
    public required string Number { get; set; }

    public bool WhatsAppEnabled { get; set; }

    public int DisplayOrder { get; set; }

    public bool IsActive { get; set; } = true;

    public virtual RestaurantInfo RestaurantInfo { get; set; } = null!;
}
