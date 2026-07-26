namespace RestaurantSystem.Domain.Common.Enums;

/// <summary>
/// Why a catalog entry cannot currently be ordered. Server-owned so the client renders ONE reason
/// instead of re-deriving precedence from several booleans (and telling a guest to "switch to
/// Takeaway" for an item that is unavailable anyway).
/// </summary>
/// <remarks>
/// Precedence, most severe first: inactive → unavailable → wrong channel.
/// <para>
/// Two axes are deliberately absent. <b>Inactive</b> (<c>Product.IsActive == false</c>) is not a
/// reason because such items are never projected to customers at all. <b>Schedule</b>
/// (<c>MenuDefinition</c> day/time windows) applies only to bundles, where
/// <c>GetMenuBundlesQuery</c> already excludes out-of-window rows upstream — so it is a filter, not
/// something to display. Plain products have no schedule concept.
/// </para>
/// </remarks>
public enum AvailabilityReason
{
    /// <summary>Orderable on the requested channel.</summary>
    Available = 0,

    /// <summary>
    /// Manually switched off (<c>Product.IsAvailable == false</c>). NOT "sold out" — there is no
    /// stock concept in this system, so the wording must not imply inventory.
    /// </summary>
    Unavailable = 1,

    /// <summary>Not offered on the requested order type, but orderable on at least one other.</summary>
    WrongOrderType = 2
}
