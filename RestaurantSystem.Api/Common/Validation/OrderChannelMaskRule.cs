using FluentValidation;

namespace RestaurantSystem.Api.Common.Validation;

/// <summary>
/// Shared FluentValidation rule for an <c>OrderChannels</c> availability mask, used by the category
/// and product create/update commands.
/// </summary>
public static class OrderChannelMaskRule
{
    /// <summary>
    /// A mask must be <c>null</c> (unrestricted) or a real <c>OrderChannels</c> subset (1..7).
    /// </summary>
    /// <remarks>
    /// Without this, a posted <c>0</c> stores "orderable on no channel", which surfaces as
    /// <c>reason=WrongOrderType</c> with an EMPTY allowed set — rendering "Available for: ." and
    /// hiding the item on every channel with no stateable reason, the exact failure
    /// <c>AvailabilityReason</c> exists to prevent. Values above 7 and negatives are equally
    /// meaningless: they block every channel (or, for -1, silently permit all of them).
    /// </remarks>
    public static IRuleBuilderOptions<T, int?> ValidOrderChannelMask<T>(this IRuleBuilder<T, int?> ruleBuilder) =>
        ruleBuilder
            .Must(mask => mask is null || (mask >= 1 && mask <= 7))
            .WithMessage("Available order types must be a combination of dine-in, takeaway or delivery");
}
