using FluentValidation;

namespace RestaurantSystem.Api.Features.Orders.Commands.AddPaymentToOrderCommand;

public class AddPaymentToOrderCommandValidator : AbstractValidator<AddPaymentToOrderCommand>
{
    public AddPaymentToOrderCommandValidator()
    {
        RuleFor(x => x.ExpectedVersion)
            .GreaterThan(0)
            .When(x => x.ExpectedVersion.HasValue)
            .WithMessage("Expected version must be greater than zero");

        RuleFor(x => x.OrderId)
            .NotEmpty()
            .WithMessage("Order ID is required");

        RuleFor(x => x.OperationId)
            .NotEmpty()
            .WithMessage("Operation ID is required");

        RuleFor(x => x.PaymentMethod)
            .IsInEnum()
            .WithMessage("Invalid payment method");

        RuleFor(x => x.Amount)
            .GreaterThan(0)
            .WithMessage("Payment amount must be greater than 0");

        RuleFor(x => x.EffectiveTipMinor)
            .InclusiveBetween(0, RestaurantSystem.Domain.Entities.OrderPayment.MaximumTipMinor)
            .WithMessage("Tip must be a non-negative minor-unit amount within the supported payment limit");
    }
}
