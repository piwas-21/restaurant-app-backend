using FluentValidation;

namespace RestaurantSystem.Api.Features.Products.Commands.SetFeaturedSpecialCommand;

public class SetFeaturedSpecialCommandValidator : AbstractValidator<SetFeaturedSpecialCommand>
{
    public SetFeaturedSpecialCommandValidator()
    {
        RuleFor(x => x.ProductId).NotEmpty().WithMessage("Product ID is required");
    }
}
