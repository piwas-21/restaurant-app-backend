namespace RestaurantSystem.Domain.Entities;

/// <summary>
/// The "focus" marker staff put on an order that needs watching, with why, when and by whom.
/// Owned by <see cref="Order"/> and stored in the same row, so <c>order.Focus is not null</c> IS
/// the flag — which is why the old <c>Orders.IsFocusOrder</c> boolean no longer exists.
/// </summary>
/// <remarks>
/// The boolean and these four columns used to be independent, so "focused but never focused-at" and
/// "not focused yet still stamped with a FocusedBy" were both representable, and un-focusing meant
/// remembering to clear four columns by hand. As an optional owned type the record is present or
/// absent as a unit. <see cref="FocusedAt"/> is non-nullable *here* precisely so EF has one column
/// whose NULL means "no focus record" — the boolean's job, done by data that cannot disagree with
/// the rest of the block.
/// </remarks>
public class OrderFocus
{
    /// <summary>
    /// 1-5, where 1 is highest. Nullable because staff can focus an order without ranking it
    /// (<c>CreateOrderCommand</c> passes the caller's value through unchanged); the focus-order
    /// list sorts those last rather than treating them as top priority.
    /// </summary>
    public int? Priority { get; set; }

    /// <summary>Free text, so it is scrubbed on GDPR erasure alongside Notes and CancellationReason.</summary>
    public string? Reason { get; set; }

    /// <summary>
    /// When the order was focused. Required within this type: it is the identifying property, so a
    /// NULL <c>FocusedAt</c> column is how EF reads the row back as "not focused".
    /// </summary>
    public DateTime FocusedAt { get; set; }

    /// <summary>User id of the staff member who focused it; null when nobody was authenticated.</summary>
    public string? FocusedBy { get; set; }
}
