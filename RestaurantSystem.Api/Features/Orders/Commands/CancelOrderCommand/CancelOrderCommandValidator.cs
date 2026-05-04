using FluentValidation;

namespace RestaurantSystem.Api.Features.Orders.Commands.CancelOrderCommand;

public class CancelOrderCommandValidator : AbstractValidator<CancelOrderCommand>
{
    public CancelOrderCommandValidator()
    {
        RuleFor(x => x.OrderId)
            .NotEmpty()
            .WithMessage("Order ID is required");

        RuleFor(x => x.CancellationReason)
            .NotEmpty()
            .MinimumLength(5)
            .WithMessage("Cancellation reason is required and must be at least 5 characters");
    }
}
