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
    }
}
