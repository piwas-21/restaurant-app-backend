using RestaurantSystem.Domain.Common.Base;
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
    /// The value a NEW row for <paramref name="orderType"/> starts with — the gating each type had
    /// before this column existed. Every creator of rows goes through here (the backfill migration
    /// duplicates it in SQL by necessity) so the default lives in one place.
    /// </summary>
    public static bool EnforcedByDefault(OrderType orderType) => orderType == OrderType.DineIn;
}
