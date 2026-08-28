using FluentValidation;
using RestaurantSystem.Api.Features.Products.Dtos;
using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.Api.Common.Validation;

/// <summary>
/// "A menu definition that is sent must carry its sections" — extracted from
/// <c>UpdateProductCommandValidator</c> by S4, unchanged in behaviour, because that validator sat at
/// exactly the CLAUDE.md §4 60-line limit and the slice had to add the variation rules to it. This
/// block was the largest thing in the file whose rationale travels with it.
///
/// <para>
/// Mirrors <c>MenuBundleCommandValidatorBase</c> (#191). <c>MenuDefinition</c> itself stays optional
/// — absent means "no menu instruction" — but once one IS sent for a <c>Menu</c>, its sections are a
/// full replace like every other field on it, so the key is required and <c>[]</c> alone clears
/// them.
/// </para>
///
/// <para>
/// This rule and the handler's section block now cover exactly the same payloads. They did not when
/// the rule was written: the block additionally sat inside a detailed-ingredients null check, so the
/// rule was deliberately WIDER than the code it protected, and #296 has since lifted the block to
/// statement level. Do NOT narrow this to re-add a <c>DetailedIngredients</c> condition — the two
/// conditions agreeing is the point, and the rule is what makes <c>command.MenuDefinition.Sections</c>
/// non-null in the handler.
/// </para>
///
/// <para>
/// Written as a <c>Must</c> on <c>MenuDefinition</c> itself, with the null case passing INSIDE the
/// predicate, so no null-forgiving operator is needed and no accessor can dereference a null.
/// </para>
/// </summary>
public static class MenuDefinitionSectionsRule
{
    public static void ValidateMenuDefinitionSections<T>(
        this AbstractValidator<T> validator,
        Func<T, MenuDefinitionDto?> menuDefinition,
        Func<T, ProductType> type)
    {
        ArgumentNullException.ThrowIfNull(validator);
        ArgumentNullException.ThrowIfNull(menuDefinition);
        ArgumentNullException.ThrowIfNull(type);

        validator.RuleFor(command => menuDefinition(command))
            .Must(definition => definition is null || definition.Sections != null)
            .WithMessage(MenuDefinitionDto.SectionsRequiredMessage)
            .When(command => type(command) == ProductType.Menu);
    }
}
