using FluentValidation;

namespace RestaurantSystem.Api.Features.Orders.Commands.CreateOrderCommand;

// FluentValidation is wired into the CustomMediator via
// ValidationBehavior<TRequest, TResponse> (Common/Behaviors/) — every
// validator registered for a command runs automatically before its handler
// and aggregated failures throw BadRequestException (→ HTTP 400).
public class CreateOrderCommandValidator : AbstractValidator<CreateOrderCommand>
{
    public CreateOrderCommandValidator()
    {
        RuleFor(c => c.Items)
            .NotEmpty()
            .WithMessage("Order must contain at least one item.");
    }
}
