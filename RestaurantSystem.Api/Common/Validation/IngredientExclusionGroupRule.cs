using System.Linq.Expressions;
using FluentValidation;
using RestaurantSystem.Api.Features.Products.Dtos;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Common.Validation;

/// <summary>
/// Shared FluentValidation rule for the ingredient mutual-exclusion groups on the product create
/// and update commands (SHARED-MODIFIERS-AND-SAUCES-PLAN §9, D13–D15).
///
/// <para>
/// Shared for the reason <see cref="SauceGroupRule"/> already records: the variation rules were once
/// applied on POST and not on PUT, so a payload that was a 400 on create reached the database on
/// edit. Both product validators also sit at the 60-line file gate, so a multi-clause rule cannot
/// live in them twice.
/// </para>
///
/// <para>
/// <b>What is deliberately NOT refused.</b> Selecting two members of a group is not a server error —
/// there is no per-group minimum and no selection check on the money path, because a payload that
/// picks both is charged for both and therefore OVERPAYS (plan D14, the same direction the sauce cap
/// already accepts). A group with a single member is legal too: it renders as an ordinary checkbox,
/// which is the honest degrade for "nothing to be exclusive with" and keeps an admin from being
/// blocked halfway through building a group.
/// </para>
/// </summary>
public static class IngredientExclusionGroupRule
{
    // Not a const: the width belongs to the entity (one number, two layers), and a const string
    // cannot interpolate one. The message names the real limit rather than repeating the digits.
    public static readonly string TooLongMessage =
        $"An ingredient group name cannot exceed {ProductIngredient.ExclusionGroupMaxLength} characters";
    public const string MixedKindMessage =
        "All ingredients in one group must be of the same kind (a group cannot mix ingredients and sauces)";
    public const string NotRemovableMessage =
        "Every ingredient in a group must be optional, otherwise the guest cannot choose between them";
    public const string ManyIncludedMessage =
        "At most one ingredient in a group may be included in the base price";

    /// <summary>
    /// The stored form of a key: trimmed, and blank becomes <c>null</c>.
    /// </summary>
    /// <remarks>
    /// The blank case is the load-bearing one. An empty string is what a cleared text input sends,
    /// and storing it would put EVERY cleared row into one anonymous group — turning "no group" into
    /// a group that silently makes unrelated ingredients exclude each other. Normalising at the two
    /// write paths (not in the validator, which may not mutate) means the database only ever holds
    /// null or a real key.
    /// </remarks>
    public static string? Normalize(string? key)
    {
        var trimmed = key?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    /// <summary>
    /// The three shapes a guest sheet could not render honestly, refused at the door.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>One kind per group</b> (plan §9 Q9): sauces render in their own section, so a group holding
    /// one sauce and one plain ingredient would be split across two blocks and could not be drawn as
    /// one choice.
    /// </para>
    /// <para>
    /// <b>Every member removable:</b> a non-optional row is a fixed part of the recipe with a
    /// disabled control, so it can never be deselected — a group containing one would let the guest
    /// end up with two members selected and no way back.
    /// </para>
    /// <para>
    /// <b>At most one member included in the base price:</b> the sheet opens with the base recipe
    /// selected, so two included members would make the OPENING state break the group's own rule
    /// before the guest touched anything. Refusing it here is what lets the client enforce
    /// exclusivity on interaction only, and never silently re-price a sheet on open.
    /// </para>
    /// </remarks>
    public static void ValidateExclusionGroups<T>(
        this AbstractValidator<T> validator,
        Expression<Func<T, List<ProductIngredientDto>?>> ingredients)
    {
        validator.RuleFor(ingredients)
            .Must(list => Groups(list).All(group => group.Select(i => i.Kind).Distinct().Count() == 1))
            .WithMessage(MixedKindMessage);

        validator.RuleFor(ingredients)
            .Must(list => Groups(list).All(group => group.All(i => i.IsOptional)))
            .WithMessage(NotRemovableMessage);

        validator.RuleFor(ingredients)
            .Must(list => Groups(list).All(group => group.Count(i => i.IsIncludedInBasePrice) <= 1))
            .WithMessage(ManyIncludedMessage);

        validator.RuleFor(ingredients)
            .Must(list => (list ?? []).All(ingredient =>
                (Normalize(ingredient.ExclusionGroup)?.Length ?? 0) <= ProductIngredient.ExclusionGroupMaxLength))
            .WithMessage(TooLongMessage);
    }

    /// <summary>The grouped rows, keyed by their NORMALISED key, ungrouped rows dropped.</summary>
    private static IEnumerable<List<ProductIngredientDto>> Groups(IEnumerable<ProductIngredientDto>? ingredients) =>
        (ingredients ?? [])
            .Select(ingredient => (Key: Normalize(ingredient.ExclusionGroup), Ingredient: ingredient))
            .Where(entry => entry.Key is not null)
            .GroupBy(entry => entry.Key, StringComparer.Ordinal)
            .Select(group => group.Select(entry => entry.Ingredient).ToList());
}
