using FluentValidation;
using RestaurantSystem.Api.Common.Validation;
using RestaurantSystem.Api.Features.Products.Dtos;
using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.Api.Features.Products.Commands.UpdateProductCommand;

public class UpdateProductCommandValidator : AbstractValidator<UpdateProductCommand>
{
    public UpdateProductCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty().WithMessage("Product ID is required");
        RuleFor(x => x.Name).NotEmpty().WithMessage("Name is required").MaximumLength(200);
        RuleFor(x => x.BasePrice).GreaterThanOrEqualTo(0).WithMessage("Price must be non-negative");
        RuleFor(x => x.PreparationTimeMinutes).GreaterThanOrEqualTo(0).WithMessage("Preparation time must be non-negative");
        RuleFor(x => x.CategoryIds).NotEmpty().WithMessage("At least one category is required");

        this.ValidatePrimaryCategory(x => x.PrimaryCategoryId, x => x.CategoryIds);
        RuleFor(x => x.AvailableOrderTypes).ValidOrderChannelMask();
        this.ValidateSauceGroup(x => x.SauceMin, x => x.SauceMax, x => x.SauceIncludedFree); // S5 / D9
        this.ValidateExclusionGroups(x => x.DetailedIngredients); // §9 / D13 — rules in the rule file

        // #306; rationale in ProductContentRule. Covers the TOP-LEVEL map only.
        RuleFor(x => x.Content).ValidProductContent(required: false);

        // #316; the two NESTED maps. Bounds and rationale in NestedContentRule.
        this.ValidateNestedContent(x => x.Variations, v => v.Content, c => c.Name, c => c.Description,
            NestedContentRule.VariationNameMaxLength);
        this.ValidateNestedContent(x => x.DetailedIngredients, i => i.Content, c => c.Name,
            c => c.Description, NestedContentRule.IngredientNameMaxLength);

        // Rationale in MenuDefinitionSectionsRule; extracted by S4 to make room for the variation
        // rules below, unchanged in behaviour.
        this.ValidateMenuDefinitionSections(x => x.MenuDefinition, x => x.Type);

        // S4, backend analysis §9 defect 1. These three clauses existed on CREATE only, so a
        // 500-character variation name was a 400 on POST and reached the database on PUT. Same rule
        // object, same messages, both paths.
        RuleForEach(x => x.Variations)
            .ChildRules(variation =>
                variation.ApplyVariationFields(v => v.Name, v => v.Description, v => v.DisplayOrder));

        // #432 — the catalogue guard for the included-in-base deduction. One line here because both
        // product validators sit at the 60-line gate; the reasoning, and why `>` and not `>=`, is in
        // IncludedInBaseDeductionRule.
        this.ValidateIncludedInBaseDeduction(x => x.BasePrice, x => x.HideBaseProduct,
            x => x.DetailedIngredients, x => x.Variations?.Where(v => v.IsActive).Select(v => v.PriceModifier));
    }
}
