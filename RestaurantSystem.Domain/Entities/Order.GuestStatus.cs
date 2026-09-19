namespace RestaurantSystem.Domain.Entities;

/// <summary>
/// The guest status poll half of <see cref="Order"/> (order confirmation flows): the checkout
/// confirmation page carries <see cref="GuestStatusToken"/> in its URL so an unauthenticated
/// guest can watch their order move Pending → Confirmed. It is deliberately a DIFFERENT secret
/// from <see cref="QuickActionToken"/>, which authorises confirm/cancel — watching your order
/// must not mean approving or cancelling it. The read-only projection it buys lives in
/// <c>GetGuestOrderStatusQuery</c>; the contract (length, entropy, lookups) is configured on
/// the entity in <c>OrderConfiguration</c>.
/// </summary>
public partial class Order
{
    /// <summary>Bearer secret for the anonymous guest status poll. Null on rows predating the
    /// feature; a null never matches, so those orders poll as "not found" exactly like unknown
    /// ids.</summary>
    public string? GuestStatusToken { get; set; }
}
