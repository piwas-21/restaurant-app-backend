using FluentValidation;

namespace RestaurantSystem.Api.Features.Basket.Commands.ClearBasketCommand;

public class ClearBasketCommandValidator : AbstractValidator<ClearBasketCommand>
{
    public ClearBasketCommandValidator()
    {
        RuleFor(x => x.SessionId).NotEmpty().WithMessage("Session ID is required");
    }
}
