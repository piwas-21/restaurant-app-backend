using System.Linq.Expressions;
using FluentValidation;

namespace RestaurantSystem.Api.Common.Validation;

/// <summary>
/// The per-variation field rules — name, description, display order — shared by the product create
/// and update commands (SHARED-MODIFIERS-AND-SAUCES-PLAN, slice S4).
///
/// <para>
/// It is shared because it was not. <c>CreateProductVariationRules.Apply</c> was called only from
/// <c>CreateProductCommandValidator</c>, so a 500-character variation name was a clean 400 on
/// <c>POST</c> and reached the database on <c>PUT</c> — a 500 once the column is bounded, and
/// silent unbounded text before that (backend analysis §9 defect 1). <c>SauceGroupRule</c> names the
/// same trap as the reason it exists; this is the slice that closes it.
/// </para>
///
/// <para>
/// The rules are passed as accessors rather than applied to a shared base type: the create and the
/// update payloads are separate records (the update one carries an <c>Id</c>), and reshaping two
/// public request contracts to fix a validator would be a far bigger change than the defect.
/// </para>
/// </summary>
public static class ProductVariationRule
{
    public const int NameMaxLength = 50;
    public const int DescriptionMaxLength = 200;

    public const string NameRequiredMessage = "Variation name is required";
    public const string NameTooLongMessage = "Variation name cannot exceed 50 characters";
    public const string DescriptionTooLongMessage = "Variation description cannot exceed 200 characters";
    public const string DisplayOrderNegativeMessage = "Variation display order cannot be negative";

    /// <summary>
    /// Applies the three clauses to one variation payload, whichever command it arrived on.
    /// </summary>
    public static void ApplyVariationFields<T>(
        this InlineValidator<T> variation,
        Expression<Func<T, string>> name,
        Expression<Func<T, string?>> description,
        Expression<Func<T, int>> displayOrder)
    {
        ArgumentNullException.ThrowIfNull(variation);

        variation.RuleFor(name)
            .NotEmpty().WithMessage(NameRequiredMessage)
            .MaximumLength(NameMaxLength).WithMessage(NameTooLongMessage);

        variation.RuleFor(description)
            .MaximumLength(DescriptionMaxLength).WithMessage(DescriptionTooLongMessage);

        variation.RuleFor(displayOrder)
            .GreaterThanOrEqualTo(0).WithMessage(DisplayOrderNegativeMessage);
    }
}
