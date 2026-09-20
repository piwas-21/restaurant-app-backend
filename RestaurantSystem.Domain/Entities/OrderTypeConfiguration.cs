using RestaurantSystem.Domain.Common.Base;
using RestaurantSystem.Domain.Common.Constants;
using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.Domain.Entities;

public class OrderTypeConfiguration : Entity
{
    public OrderType OrderType { get; set; }
    public bool IsEnabled { get; set; }
    public int DisplayOrder { get; set; }

    /// <summary>
    /// Whether this order type is refused while the restaurant is OUTSIDE its working hours — the
    /// type vanishes from the guest's offered set, exactly as DineIn always has (#448). Inside
    /// hours this column changes nothing.
    /// <para>
    /// Defaults reproduce the pre-#448 behaviour per type: DineIn was the only gated type, so only
    /// DineIn starts at <c>true</c> (see <see cref="EnforcedByDefault"/>). The setting makes gating
    /// AVAILABLE; it does not switch it on — no tenant may silently lose overnight takeaway orders.
    /// </para>
    /// </summary>
    public bool EnforceOpeningHours { get; set; }

    /// <summary>
    /// What happens after a guest places an order of this type (cashier POS redesign, order
    /// confirmation flows): <c>direct</c> is the historical behaviour — the kitchen just starts
    /// working, the guest's mail says "pending confirmation". <c>acknowledge</c> is the reviewed
    /// hand-off — the guest is told the order was RECEIVED and is under review for about
    /// <see cref="ReviewWindowMinutes"/> minutes, and a cashier explicitly approves it (with a
    /// preparation time) before the kitchen starts. DineIn is unaffected: table orders
    /// auto-confirm at creation either way.
    /// </summary>
    public string ConfirmationFlow { get; set; } = OrderConfirmationFlows.Direct;

    /// <summary>
    /// The review window the acknowledge flow promises the guest, in minutes. Only read when
    /// <see cref="ConfirmationFlow"/> is <c>acknowledge</c>.
    /// </summary>
    public int ReviewWindowMinutes { get; set; } = DefaultReviewWindowMinutes;

    /// <summary>
    /// The value a NEW row for <paramref name="orderType"/> starts with — the gating each type had
    /// before this column existed. Every creator of rows goes through here (the backfill migration
    /// duplicates it in SQL by necessity) so the default lives in one place.
    /// </summary>
    public static bool EnforcedByDefault(OrderType orderType) => orderType == OrderType.DineIn;

    /// <summary>Backfill default for the flow column: every existing tenant keeps today's behaviour.</summary>
    public const string DefaultConfirmationFlow = OrderConfirmationFlows.Direct;

    /// <summary>Backfill default for the review window (the owner-specified 2 minutes).</summary>
    public const int DefaultReviewWindowMinutes = 2;

    /// <summary>Upper bound for the review window — a "few minutes" promise, not a reservation slot.</summary>
    public const int MaxReviewWindowMinutes = 60;
}
