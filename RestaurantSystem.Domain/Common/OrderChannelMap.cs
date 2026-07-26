using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.Domain.Common;

/// <summary>
/// The single, authoritative conversion between <see cref="OrderType"/> and the
/// <see cref="OrderChannels"/> bitmask, plus the permissive-null availability rule.
/// </summary>
/// <remarks>
/// Direct casts between the two enums are FORBIDDEN (see <see cref="OrderChannels"/>). Every
/// conversion in the codebase must route through this class so the round-trip test is the only
/// thing that has to be correct.
/// <para>
/// Storage convention: a <c>null</c> mask means "available on every channel". Storing null rather
/// than <see cref="OrderChannels.All"/> keeps the migration backfill-free — existing rows are
/// unrestricted by default — and mirrors <c>MenuDefinition.IsAlwaysAvailable</c> semantics.
/// </para>
/// </remarks>
public static class OrderChannelMap
{
    /// <summary>The single-channel mask for one order type.</summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The order type is not a known value — fail loudly rather than silently returning
    /// <see cref="OrderChannels.None"/>, which would block every item.
    /// </exception>
    public static OrderChannels From(OrderType orderType) => orderType switch
    {
        OrderType.DineIn => OrderChannels.DineIn,
        OrderType.Takeaway => OrderChannels.Takeaway,
        OrderType.Delivery => OrderChannels.Delivery,
        _ => throw new ArgumentOutOfRangeException(nameof(orderType), orderType, "Unknown order type.")
    };

    /// <summary>The order types present in a mask, in <see cref="OrderType"/> declaration order.</summary>
    public static IReadOnlyList<OrderType> ToOrderTypes(OrderChannels channels)
    {
        var result = new List<OrderType>(3);
        foreach (var orderType in Enum.GetValues<OrderType>())
        {
            if (channels.HasFlag(From(orderType)))
            {
                result.Add(orderType);
            }
        }

        return result;
    }

    /// <summary>
    /// The order types a stored mask permits. A <c>null</c> mask is unrestricted, so this returns
    /// every order type.
    /// </summary>
    public static IReadOnlyList<OrderType> ToOrderTypes(int? storedMask) =>
        storedMask is null
            ? Enum.GetValues<OrderType>()
            : ToOrderTypes((OrderChannels)storedMask.Value);

    /// <summary>
    /// Whether a stored mask permits an order type. A <c>null</c> mask is unrestricted — baskets
    /// are created with no order type and an unchosen channel is the dominant browse state, so the
    /// permissive answer is the correct one.
    /// </summary>
    public static bool Allows(int? storedMask, OrderType orderType) =>
        storedMask is null || ((OrderChannels)storedMask.Value).HasFlag(From(orderType));

    /// <summary>The stored representation of a channel set — <c>null</c> when unrestricted.</summary>
    public static int? ToStoredMask(OrderChannels channels) =>
        channels == OrderChannels.All ? null : (int)channels;
}
