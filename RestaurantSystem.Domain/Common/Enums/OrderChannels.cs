namespace RestaurantSystem.Domain.Common.Enums;

/// <summary>
/// The set of order types a catalog entry (category or product) may be ordered through, as a
/// bitmask. Deliberately SEPARATE from <see cref="OrderType"/>: that enum is 1/2/3, so its values
/// are not power-of-two and cannot be OR-ed into a set.
/// </summary>
/// <remarks>
/// NEVER cast between this and <see cref="OrderType"/> directly — <c>(int)OrderType.Delivery</c> is
/// 3, which as a mask reads as <c>DineIn | Takeaway</c>. That is a *legal* mask value, so a stray
/// cast throws nothing and fails no type check; it silently returns the wrong answer. Go through
/// <see cref="OrderChannelMap"/>, which is covered by a round-trip test over every value.
/// </remarks>
[Flags]
public enum OrderChannels
{
    None = 0,
    DineIn = 1,
    Takeaway = 2,
    Delivery = 4,

    /// <summary>Every channel. Stored as <c>null</c> rather than this value — see OrderChannelMap.</summary>
    All = DineIn | Takeaway | Delivery
}
