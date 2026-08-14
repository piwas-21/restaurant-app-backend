namespace RestaurantSystem.Domain.Common.Constants;

/// <summary>
/// The <c>EmailType</c> half of an <see cref="RestaurantSystem.Domain.Entities.OutboundEmail"/>
/// claim. String rather than an enum because these values are persisted and read by humans in
/// support queries; renaming an enum member would silently re-arm a mail that had already been
/// sent for every row written under the old name.
/// </summary>
public static class OutboundEmailTypes
{
    /// <summary>M7 — the guest's "we have your order" receipt.</summary>
    public const string OrderReceived = "order.received";

    /// <summary>M14 — the restaurant's new-order alert with the confirm/cancel buttons.</summary>
    public const string OrderAdminAlert = "order.admin-alert";
}
