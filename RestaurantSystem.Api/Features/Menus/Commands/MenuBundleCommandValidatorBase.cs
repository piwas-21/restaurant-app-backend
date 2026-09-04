using FluentValidation;
using RestaurantSystem.Api.Common.Validation;
using RestaurantSystem.Api.Features.Products.Dtos;

namespace RestaurantSystem.Api.Features.Menus.Commands;

/// <summary>
/// The rules shared by the create and update menu-bundle commands (#156). Each command's
/// validator derives from this and adds only its own, so the common ones live in one place.
/// </summary>
public abstract class MenuBundleCommandValidatorBase<T> : AbstractValidator<T> where T : IMenuBundleCommandFields
{
    protected MenuBundleCommandValidatorBase()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Menu bundle name is required")
            .MaximumLength(100).WithMessage("Name cannot exceed 100 characters");

        RuleFor(x => x.Description)
            .MaximumLength(500).WithMessage("Description cannot exceed 500 characters");

        RuleFor(x => x.BasePrice)
            .GreaterThan(0).WithMessage("Base price must be greater than 0");

        RuleFor(x => x.PreparationTimeMinutes)
            .GreaterThanOrEqualTo(0).WithMessage("Preparation time cannot be negative");

        RuleFor(x => x.DisplayOrder)
            .GreaterThanOrEqualTo(0).WithMessage("Display order cannot be negative");

        RuleFor(x => x.MenuDefinition)
            .NotNull().WithMessage("Menu definition is required");

        // Required, and the .When() is load-bearing rather than defensive — MenuDefinitionDto.Sections (#191).
        RuleFor(x => x.MenuDefinition.Sections).NotNull()
            .WithMessage(MenuDefinitionDto.SectionsRequiredMessage).When(x => x.MenuDefinition != null);

        RuleFor(x => x.CategoryIds)
            .Must(x => x == null || x.Distinct().Count() == x.Count).WithMessage("Duplicate categories are not allowed");

        RuleFor(x => x.PrimaryCategoryId)
            .Must((command, primaryCategoryId) =>
                !primaryCategoryId.HasValue || (command.CategoryIds != null && command.CategoryIds.Contains(primaryCategoryId.Value)))
            .WithMessage("Primary category must be one of the selected categories");

        // Required only WHEN categories are sent — deliberately weaker than the product validator's
        // unconditional NotNull. The update handler rebuilds ProductCategories from a non-empty
        // CategoryIds, so a null primary in that payload un-primaries the bundle and kills its
        // inheritance (§3.4); but an absent/empty list means "no category instruction" and leaves
        // the existing rows (primary flag included) alone — #190, pinned by
        // UpdateMenuBundlePreservesAssignmentsTests. The bundle editor sends exactly that, so an
        // unconditional rule would 400 every save from the only client there is.
        RuleFor(x => x.PrimaryCategoryId)
            .NotNull().WithMessage("A primary category is required when categories are sent")
            .When(x => x.CategoryIds?.Count > 0);

        RuleFor(x => x.AvailableOrderTypes).ValidOrderChannelMask();
        RuleFor(x => x.Allergens).ValidAllergenList();
    }
}
