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
        // The condition is deliberately WIDER than the code it protects: the handler's section
        // block additionally sits inside `if (command.DetailedIngredients != null)` (see #296), so
        // a Menu-type payload that omits detailedIngredients now 400s where it previously 200'd
        // without touching sections. Unreachable from the admin editor, which always sends the
        // array — and narrowing this rule to match would make it silently stop protecting the
        // moment that nesting is corrected.
        //
        // The `!` is the .When() guard restated for the compiler: FluentValidation forces the
        // property accessor only after the condition passes, so it never dereferences a null.
        RuleFor(x => x.MenuDefinition!.Sections)
            .NotNull().WithMessage(MenuDefinitionDto.SectionsRequiredMessage)
            .When(x => x.MenuDefinition != null && x.Type == ProductType.Menu);
    }
}
