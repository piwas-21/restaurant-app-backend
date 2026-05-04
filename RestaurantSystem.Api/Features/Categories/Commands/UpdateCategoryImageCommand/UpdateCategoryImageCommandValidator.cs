using FluentValidation;

namespace RestaurantSystem.Api.Features.Categories.Commands.UpdateCategoryImageCommand;

public class UpdateCategoryImageCommandValidator : AbstractValidator<UpdateCategoryImageCommand>
{
    public UpdateCategoryImageCommandValidator()
    {
        RuleFor(x => x.CategoryId).NotEmpty().WithMessage("Category ID is required");
    }
}
