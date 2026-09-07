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

        // Both columns the reason is written to are varchar(500): Order.CancellationReason
        // (OrderConfiguration.cs) and OrderStatusHistory.Notes (OrderStatusHistoryConfiguration.cs).
        // A separate rule, not a chain member: WithMessage applies backwards to every validator
        // chained before it, so appending here would have reworded the floor message above.
        // Without this ceiling a longer reason surfaced as Postgres 22001 out of SaveChangesAsync
        // — a 500 with the order left open (#340) — instead of this stateable 400.
        RuleFor(x => x.CancellationReason)
            .MaximumLength(500)
            .WithMessage("Cancellation reason cannot exceed 500 characters");
    }
}
