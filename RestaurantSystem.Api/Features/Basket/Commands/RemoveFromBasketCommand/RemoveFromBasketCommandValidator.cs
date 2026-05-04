using FluentValidation;

namespace RestaurantSystem.Api.Features.Basket.Commands.RemoveFromBasketCommand;

public class RemoveFromBasketCommandValidator : AbstractValidator<RemoveFromBasketCommand>
{
    public RemoveFromBasketCommandValidator()
    {
        RuleFor(x => x.SessionId).NotEmpty().WithMessage("Session ID is required");
        RuleFor(x => x.BasketItemId).NotEmpty().WithMessage("Basket item ID is required");
    }
}
