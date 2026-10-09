using FluentValidation;

namespace RestaurantSystem.Api.Features.Orders.Commands.RefundPaymentCommand;

public class RefundPaymentCommandValidator : AbstractValidator<RefundPaymentCommand>
{
    public RefundPaymentCommandValidator()
    {
        RuleFor(x => x.ExpectedVersion)
            .GreaterThan(0)
            .When(x => x.ExpectedVersion.HasValue)
            .WithMessage("Expected version must be greater than zero");

        RuleFor(x => x.OrderId)
            .NotEmpty()
            .WithMessage("Order ID is required");

        RuleFor(x => x.PaymentId)
            .NotEmpty()
            .WithMessage("Payment ID is required");

        RuleFor(x => x.RefundAmount)
            .GreaterThanOrEqualTo(0)
            .WithMessage("Refund amount cannot be negative");

        RuleFor(x => x.EffectiveRefundTipMinor)
            .InclusiveBetween(0, RestaurantSystem.Domain.Entities.OrderPayment.MaximumTipMinor)
            .WithMessage("Refunded tip must be a non-negative minor-unit amount within the payment limit");

        RuleFor(x => x)
            .Must(command => command.RefundAmount > 0 || command.EffectiveRefundTipMinor > 0)
            .WithMessage("Enter a positive food refund or tip refund amount");

        RuleFor(x => x.RefundReason)
            .NotEmpty()
            .MinimumLength(5)
            .WithMessage("Refund reason is required and must be at least 5 characters");
    }
}
