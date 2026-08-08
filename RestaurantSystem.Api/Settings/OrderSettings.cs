namespace RestaurantSystem.Api.Settings;

/// <summary>
/// Order-level pricing configuration. Bound from the <c>OrderSettings</c> configuration section.
/// </summary>
public class OrderSettings
{
    public const string SectionName = "OrderSettings";

    /// <summary>
    /// Flat fee added to <see cref="Domain.Common.Enums.OrderType.Delivery"/> orders.
    /// <para>
    /// Defaults to <b>0</b> deliberately. The previous hard-coded 5.00 constant only ever fired on
    /// the legacy compute path, which no client uses — every real customer order came through
    /// <c>/from-basket</c>, where the fee was never applied at all. Making the server authoritative
    /// over totals would therefore have silently started charging a fee no live tenant charges
    /// today, so the default preserves what customers actually pay and a tenant opts in per box.
    /// </para>
    /// </summary>
    public decimal DeliveryFee { get; set; }
}
