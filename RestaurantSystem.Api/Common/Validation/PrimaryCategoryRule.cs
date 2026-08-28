using System.Linq.Expressions;
using FluentValidation;

namespace RestaurantSystem.Api.Common.Validation;

/// <summary>
/// The primary-category clauses shared by the product create and update commands.
///
/// <para>
/// A primary category is REQUIRED, not merely validated when present. Both handlers rebuild
/// <c>ProductCategories</c> on every save (<c>RemoveRange</c> + recreate), so a null primary
/// silently un-primaries the product — and a product inherits its order-type availability from its
/// primary category (ORDER-TYPE-AVAILABILITY-PLAN §3.4), so losing it changes where the product can
/// be ordered.
/// </para>
///
/// <para>
/// Extracted by #432 to make room under the 60-line <c>*Validator.cs</c> gate, unchanged in
/// behaviour and message text — the same move, for the same reason, that S4 made for the menu
/// definition rules. It was written twice and identically in the two validators, which is exactly
/// the drift <see cref="ProductVariationRule"/> exists because of.
/// </para>
/// </summary>
public static class PrimaryCategoryRule
{
    public const string RequiredMessage = "A primary category is required";
    public const string NotSelectedMessage = "Primary category must be one of the selected categories";

    public static void ValidatePrimaryCategory<T>(
        this AbstractValidator<T> validator,
        Expression<Func<T, Guid?>> primaryCategoryId,
        Func<T, List<Guid>> categoryIds)
    {
        ArgumentNullException.ThrowIfNull(validator);
        ArgumentNullException.ThrowIfNull(categoryIds);

        validator.RuleFor(primaryCategoryId)
            .NotNull().WithMessage(RequiredMessage)
            .Must((command, primary) => !primary.HasValue || categoryIds(command).Contains(primary.Value))
            .WithMessage(NotSelectedMessage);
    }
}
