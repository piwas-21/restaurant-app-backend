using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.Api.Features.Catalog.Dtos;

/// <summary>
/// The single resolved answer to "can the guest order this right now, and if not why?" — computed
/// server-side by <see cref="OrderTypeAvailability"/> so every client renders one reason rather than
/// re-deriving precedence from <c>IsActive</c>/<c>IsAvailable</c>/schedule/channel.
/// </summary>
public record ItemAvailabilityDto
{
    /// <summary>True when the item can be added to the basket for the requested order type.</summary>
    public bool CanOrder { get; init; }

    /// <summary>Why not, when <see cref="CanOrder"/> is false.</summary>
    public AvailabilityReason Reason { get; init; }

    /// <summary>
    /// Every order type this item IS available on — drives the customer-facing chip ("Takeaway &amp;
    /// delivery only") and the one-tap "Switch to Takeaway" CTA. Unrestricted items list all three.
    /// </summary>
    public IReadOnlyList<OrderType> AllowedOrderTypes { get; init; } = [];

    /// <summary>
    /// True when this item's channel set is inherited from its primary category rather than set on
    /// the item. Admin-facing only — the editor renders "Inherit" vs "Custom" from this.
    /// </summary>
    public bool InheritsOrderTypes { get; init; }
}
