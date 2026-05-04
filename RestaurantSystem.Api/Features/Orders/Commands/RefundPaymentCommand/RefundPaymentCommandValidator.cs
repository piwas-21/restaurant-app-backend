using FluentValidation;

namespace RestaurantSystem.Api.Features.Orders.Commands.RefundPaymentCommand;

public class RefundPaymentCommandValidator : AbstractValidator<RefundPaymentCommand>
{
    public RefundPaymentCommandValidator()
    {
        RuleFor(x => x.OrderId)
            .NotEmpty()
            .WithMessage("Order ID is required");

        RuleFor(x => x.PaymentId)
            .NotEmpty()
            .WithMessage("Payment ID is required");

        RuleFor(x => x.RefundAmount)
            .GreaterThan(0)
            .WithMessage("Refund amount must be greater than 0");

        RuleFor(x => x.RefundReason)
            .NotEmpty()
            .MinimumLength(5)
            .WithMessage("Refund reason is required and must be at least 5 characters");
    }
}
