using FluentValidation;

namespace RestaurantSystem.Api.Features.Basket.Commands.UpdateBasketItemCommand;

public class UpdateBasketItemCommandValidator : AbstractValidator<UpdateBasketItemCommand>
{
    public UpdateBasketItemCommandValidator()
    {
        RuleFor(x => x.SessionId)
            .NotEmpty().WithMessage("Session ID is required");

        RuleFor(x => x.BasketItemId)
            .NotEmpty().WithMessage("Basket item ID is required");

        RuleFor(x => x.Quantity)
            .GreaterThan(0).WithMessage("Quantity must be greater than 0")
            .LessThanOrEqualTo(100).WithMessage("Quantity cannot exceed 100");

        RuleFor(x => x.SpecialInstructions)
            .MaximumLength(500).WithMessage("Special instructions cannot exceed 500 characters");
    }
}
