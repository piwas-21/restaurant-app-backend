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

        // A primary category is REQUIRED here, not merely validated when present. The handler
        // rebuilds ProductCategories on every save (RemoveRange + recreate), so a null primary
        // silently un-primaries the product — and products inherit order-type availability from
        // their primary category (ORDER-TYPE-AVAILABILITY-PLAN §3.4).
        RuleFor(x => x.PrimaryCategoryId)
            .NotNull().WithMessage("A primary category is required")
            .Must((command, primaryCategoryId) =>
                !primaryCategoryId.HasValue || command.CategoryIds.Contains(primaryCategoryId.Value))
            .WithMessage("Primary category must be one of the selected categories");
        RuleFor(x => x.AvailableOrderTypes).ValidOrderChannelMask();

        // Mirrors MenuBundleCommandValidatorBase (#191). MenuDefinition itself stays optional here
        // — absent means "no menu instruction" — but once one IS sent for a Menu, its sections are
        // a full replace like every other field on it, so the key is required and `[]` alone
        // clears them.
        //
        // This rule and the handler's section block now cover exactly the same payloads. They did
        // not when the rule was written: the block additionally sat inside a detailed-ingredients
        // null check, so the rule was deliberately WIDER than the code it protected, and #296 has
        // since lifted the block to statement level. Do NOT narrow this to re-add a
        // DetailedIngredients condition — the two conditions agreeing is the point, and the rule is
        // what makes `command.MenuDefinition.Sections` non-null in the handler.
        //
        // Written as a Must on MenuDefinition itself, with the null case passing INSIDE the
        // predicate, so no null-forgiving operator is needed and no accessor can dereference a
        // null: MenuDefinition stays optional here (absent = "no menu instruction"), and only a
        // definition that IS sent must carry its sections.
        RuleFor(x => x.MenuDefinition)
            .Must(menuDefinition => menuDefinition is null || menuDefinition.Sections != null)
            .WithMessage(MenuDefinitionDto.SectionsRequiredMessage)
            .When(x => x.Type == ProductType.Menu);
    }
}
