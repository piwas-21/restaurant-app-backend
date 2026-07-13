using FluentValidation;

namespace RestaurantSystem.Api.Features.Menus.Commands.CreateMenuBundleCommand;

public class CreateMenuBundleCommandValidator : AbstractValidator<CreateMenuBundleCommand>
{
    public CreateMenuBundleCommandValidator()
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

        RuleFor(x => x.CategoryIds)
             .Must(x => x == null || x.Distinct().Count() == x.Count).WithMessage("Duplicate categories are not allowed");

        RuleFor(x => x.PrimaryCategoryId)
            .Must((command, primaryCategoryId) =>
                !primaryCategoryId.HasValue || (command.CategoryIds != null && command.CategoryIds.Contains(primaryCategoryId.Value)))
            .WithMessage("Primary category must be one of the selected categories");
    }
}
