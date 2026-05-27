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

        // Per-item rules — guard against tampered payloads with non-positive
        // quantities (would manipulate totals or bypass payment) and items
        // missing both ProductId and MenuId (would silently no-op in the
        // OrderItemFactory and create empty orders). PR #67 security review.
        RuleForEach(c => c.Items).ChildRules(item =>
        {
            item.RuleFor(i => i.Quantity)
                .GreaterThan(0)
                .WithMessage("Item quantity must be greater than zero.");

            item.RuleFor(i => i)
                .Must(i => i.ProductId.HasValue || i.MenuId.HasValue)
                .WithMessage("Each item must reference either a ProductId or a MenuId.");
        });
    }
}
