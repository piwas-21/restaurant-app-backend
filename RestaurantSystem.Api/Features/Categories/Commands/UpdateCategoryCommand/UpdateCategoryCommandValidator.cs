using FluentValidation;

namespace RestaurantSystem.Api.Features.Categories.Commands.UpdateCategoryCommand;

public class UpdateCategoryCommandValidator : AbstractValidator<UpdateCategoryCommand>
{
    public UpdateCategoryCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty().WithMessage("Category ID is required");
        RuleFor(x => x.Name).NotEmpty().WithMessage("Name is required").MaximumLength(100);
        RuleFor(x => x.DisplayOrder).GreaterThanOrEqualTo(0).WithMessage("Display order must be non-negative");
    }
}
