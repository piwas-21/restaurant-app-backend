using FluentValidation;

namespace RestaurantSystem.Api.Features.Orders.Commands.ToggleFocusOrderCommand;

public class ToggleFocusOrderCommandValidator : AbstractValidator<ToggleFocusOrderCommand>
{
    public ToggleFocusOrderCommandValidator()
    {
        RuleFor(x => x.OrderId)
            .NotEmpty()
            .WithMessage("Order ID is required");

        RuleFor(x => x.Priority)
            .InclusiveBetween(1, 5)
            .When(x => x.IsFocusOrder && x.Priority.HasValue)
            .WithMessage("Priority must be between 1 and 5");

        RuleFor(x => x.FocusReason)
            .NotEmpty()
            .When(x => x.IsFocusOrder)
            .WithMessage("Focus reason is required when marking as focus order");
    }
}
