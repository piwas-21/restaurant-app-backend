using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.Api.Features.Catalog.Dtos;

/// <summary>
/// The single resolved answer to "can the guest order this right now, and if not why?" — computed
/// server-side by <see cref="OrderTypeAvailability"/> so every client renders one reason rather than
/// re-deriving precedence from <c>IsActive</c>/<c>IsAvailable</c>/schedule/channel.
/// </summary>
public record ItemAvailabilityDto
{
    /// <summary>
    /// True when the item can be added to the basket for the requested order type.
    /// </summary>
    /// <remarks>
    /// Defaults to <c>true</c>, and <see cref="AllowedOrderTypes"/> defaults to every order type, so
    /// a projection that forgets to set this field yields the PERMISSIVE answer. The alternative
    /// default (<c>false</c> with <c>Reason = Available</c>) is self-contradictory: the client would
    /// dim an item for no stateable reason. Permissive-on-missing-data is this feature's invariant
    /// everywhere else, so the default matches it.
    /// </remarks>
    public bool CanOrder { get; init; } = true;

    /// <summary>Why not, when <see cref="CanOrder"/> is false.</summary>
    public AvailabilityReason Reason { get; init; }

    /// <summary>
    /// Every order type this item IS available on — drives the customer-facing chip ("Takeaway &amp;
    /// delivery only") and the one-tap "Switch to Takeaway" CTA. Unrestricted items list all three.
    /// </summary>
    public IReadOnlyList<OrderType> AllowedOrderTypes { get; init; } = Enum.GetValues<OrderType>();

    /// <summary>
    /// True when this item's channel set is inherited from its primary category rather than set on
    /// the item. Admin-facing only — the editor renders "Inherit" vs "Custom" from this.
    /// </summary>
    public bool InheritsOrderTypes { get; init; }
}
