using FluentValidation;
using RestaurantSystem.Api.Features.Orders.Commands.SubmitCustomerCheckoutCommand;

namespace RestaurantSystem.Api.Features.Orders.Commands.RecoverCustomerPaymentCommand;

public sealed class RecoverCustomerPaymentCommandValidator : AbstractValidator<RecoverCustomerPaymentCommand>
{
    public RecoverCustomerPaymentCommandValidator()
    {
        RuleFor(value => value.SessionId).NotEmpty();
        RuleFor(value => value.Proof).NotNull().SetValidator(new CustomerOrderRecoveryValidator());
    }
}
