using FluentValidation;
using RestaurantSystem.Api.Common.Validation;
using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.Api.Features.Products.Commands.CreateProductCommand;

public class CreateProductCommandValidator : AbstractValidator<CreateProductCommand>
{
    public CreateProductCommandValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Product name is required")
            .MaximumLength(100).WithMessage("Product name cannot exceed 100 characters");

        RuleFor(x => x.Description)
            .MaximumLength(500).WithMessage("Description cannot exceed 500 characters");

        RuleFor(x => x.BasePrice)
            .GreaterThan(0).WithMessage("Base price must be greater than 0");

        RuleFor(x => x.PreparationTimeMinutes)
            .GreaterThanOrEqualTo(0).WithMessage("Preparation time cannot be negative");

        RuleFor(x => x.Type)
            .IsInEnum().WithMessage("Invalid product type")
            .NotEqual(ProductType.Menu).WithMessage("Use CreateMenuBundle API for creating menus");

        RuleFor(x => x.DisplayOrder)
            .GreaterThanOrEqualTo(0).WithMessage("Display order cannot be negative");

        RuleFor(x => x.CategoryIds)
            .NotEmpty().WithMessage("At least one category must be selected")
            .Must(x => x.Distinct().Count() == x.Count).WithMessage("Duplicate categories are not allowed");

        this.ValidatePrimaryCategory(x => x.PrimaryCategoryId, x => x.CategoryIds);

        RuleForEach(x => x.Variations).ChildRules(CreateProductVariationRules.Apply);

        RuleFor(x => x.SuggestedSideItemIds)
            .Must(x => x == null || x.Distinct().Count() == x.Count)
            .WithMessage("Duplicate side items are not allowed");
        RuleFor(x => x.AvailableOrderTypes).ValidOrderChannelMask();
        this.ValidateSauceGroup(x => x.SauceMin, x => x.SauceMax, x => x.SauceIncludedFree); // S5 / D9

        // #306; required: true is CREATE-only — see ProductContentRule.
        RuleFor(x => x.Content).ValidProductContent(required: true);

        // #316; the two NESTED maps. Bounds and rationale in NestedContentRule.
        this.ValidateNestedContent(x => x.Variations, v => v.Content, c => c.Name, c => c.Description,
            NestedContentRule.VariationNameMaxLength);
        this.ValidateNestedContent(x => x.DetailedIngredients, i => i.Content, c => c.Name,
            c => c.Description, NestedContentRule.IngredientNameMaxLength);

        // #432 — the catalogue guard for the included-in-base deduction; why `>` and not `>=` is in
        // IncludedInBaseDeductionRule.
        this.ValidateIncludedInBaseDeduction(x => x.BasePrice, x => x.HideBaseProduct,
            x => x.DetailedIngredients, x => x.Variations?.Where(v => v.IsActive).Select(v => v.PriceModifier));
    }
}
