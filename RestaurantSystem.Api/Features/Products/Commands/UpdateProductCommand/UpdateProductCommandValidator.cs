using FluentValidation;

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
    }
}
