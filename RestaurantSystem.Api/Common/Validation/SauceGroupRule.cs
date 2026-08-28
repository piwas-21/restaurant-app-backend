using System.Linq.Expressions;
using FluentValidation;

namespace RestaurantSystem.Api.Common.Validation;

/// <summary>
/// Shared FluentValidation rule for the sauce group numbers on the product create and update
/// commands (SHARED-MODIFIERS-AND-SAUCES-PLAN S5 / D9).
///
/// <para>
/// Shared rather than written twice for a reason the repo has already paid for once: the variation
/// rules were applied on POST and NOT on PUT, so a payload that was a 400 on create was a 500 on
/// edit (plan §5, slice S4). Two validators of ~60 LOC each cannot hold a five-clause rule without
/// drifting, and the file-length gate for a <c>*Validator.cs</c> is 60 lines, so the rule lives
/// here — where the limit is 300 and the reasoning fits next to the code.
/// </para>
///
/// <para>
/// <b>What is NOT here.</b> No default is applied. The owner ruled (plan §7 Q3, 2026-08-27) that
/// these three are admin-editable per product with no tenant rule baked in, so validation refuses
/// nonsense and decides nothing: a product that never mentions them keeps 0 / null / 0, which is
/// exactly what it had before this slice existed.
/// </para>
/// </summary>
public static class SauceGroupRule
{
    public const string MinNegativeMessage = "The minimum number of sauces cannot be negative";
    public const string MaxNegativeMessage = "The maximum number of sauces cannot be negative";
    public const string IncludedFreeNegativeMessage = "The number of free sauces cannot be negative";
    public const string MinAboveMaxMessage = "The minimum number of sauces cannot exceed the maximum";
    public const string IncludedFreeAboveMaxMessage = "The number of free sauces cannot exceed the maximum";

    /// <summary>
    /// Applies the five clauses to a command carrying <c>SauceMin</c>, <c>SauceMax</c> and
    /// <c>SauceIncludedFree</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>A null maximum is "no group cap", and the two cross-field clauses therefore do not apply
    /// to it.</b> That is not leniency: with no cap there is no number for a minimum or a free
    /// allowance to exceed. It is also why the maximum is <c>int?</c> rather than an <c>int</c>
    /// where 0 means unlimited — 0 is a meaningful cap (a product that takes no sauces), so
    /// overloading it would make "no sauces allowed" and "unlimited sauces" the same payload.
    /// </para>
    /// <para>
    /// The negative clauses are separate from the cross-field ones on purpose, so a caller sending
    /// <c>min = -1</c> is told that, and not the confusing "minimum cannot exceed the maximum" that
    /// a single combined predicate would produce.
    /// </para>
    /// </remarks>
    public static void ValidateSauceGroup<T>(
        this AbstractValidator<T> validator,
        Expression<Func<T, int>> min,
        Expression<Func<T, int?>> max,
        Expression<Func<T, int>> includedFree)
    {
        var minValue = min.Compile();
        var includedFreeValue = includedFree.Compile();

        validator.RuleFor(min).GreaterThanOrEqualTo(0).WithMessage(MinNegativeMessage);
        validator.RuleFor(includedFree).GreaterThanOrEqualTo(0).WithMessage(IncludedFreeNegativeMessage);
        validator.RuleFor(max).GreaterThanOrEqualTo(0).WithMessage(MaxNegativeMessage);

        validator.RuleFor(max)
            .Must((command, cap) => cap is null || minValue(command) <= cap.Value)
            .WithMessage(MinAboveMaxMessage);

        validator.RuleFor(max)
            .Must((command, cap) => cap is null || includedFreeValue(command) <= cap.Value)
            .WithMessage(IncludedFreeAboveMaxMessage);
    }
}
