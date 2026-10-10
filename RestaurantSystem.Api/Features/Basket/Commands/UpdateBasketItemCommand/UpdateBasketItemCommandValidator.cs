using FluentValidation;
using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Settings;

namespace RestaurantSystem.Api.Features.Basket.Commands.UpdateBasketItemCommand;

public class UpdateBasketItemCommandValidator : AbstractValidator<UpdateBasketItemCommand>
{
    public UpdateBasketItemCommandValidator(IOptions<BasketSettings>? basketSettings = null)
    {
        var maxQuantityPerItem = (basketSettings?.Value ?? new BasketSettings()).MaxQuantityPerItem;

        RuleFor(x => x.SessionId)
            .NotEmpty().WithMessage("Session ID is required");

        RuleFor(x => x.BasketItemId)
            .NotEmpty().WithMessage("Basket item ID is required");

        RuleFor(x => x.Quantity)
            .GreaterThan(0).WithMessage("Quantity must be greater than 0")
            .LessThanOrEqualTo(maxQuantityPerItem)
            .WithMessage($"Quantity cannot exceed {maxQuantityPerItem}");

        RuleFor(x => x.SpecialInstructions)
            .MaximumLength(500).WithMessage("Special instructions cannot exceed 500 characters");
    }
}
