using FluentValidation;

namespace RestaurantSystem.Api.Features.Orders.Commands.AddPaymentToOrderCommand;

public class AddPaymentToOrderCommandValidator : AbstractValidator<AddPaymentToOrderCommand>
{
    public AddPaymentToOrderCommandValidator()
    {
        RuleFor(x => x.OrderId)
            .NotEmpty()
            .WithMessage("Order ID is required");

        RuleFor(x => x.PaymentMethod)
            .IsInEnum()
            .WithMessage("Invalid payment method");

        RuleFor(x => x.Amount)
            .GreaterThan(0)
            .WithMessage("Payment amount must be greater than 0");
    }
}
