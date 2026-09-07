using FluentValidation;

namespace RestaurantSystem.Api.Features.Orders.Commands.AddTableBillPaymentCommand;

public class AddTableBillPaymentCommandValidator : AbstractValidator<AddTableBillPaymentCommand>
{
    public AddTableBillPaymentCommandValidator()
    {
        RuleFor(x => x.TableNumber)
            .GreaterThan(0)
            .WithMessage("Table number must be greater than 0");

        RuleFor(x => x.PaymentMethod)
            .IsInEnum()
            .WithMessage("Invalid payment method");

        RuleFor(x => x.Amount)
            .GreaterThan(0)
            .WithMessage("Payment amount must be greater than 0");
    }
}
