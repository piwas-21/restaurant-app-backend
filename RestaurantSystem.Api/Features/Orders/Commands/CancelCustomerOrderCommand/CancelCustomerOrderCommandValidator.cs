using FluentValidation;
using RestaurantSystem.Api.Features.Orders.Commands.SubmitCustomerCheckoutCommand;

namespace RestaurantSystem.Api.Features.Orders.Commands.CancelCustomerOrderCommand;

public sealed class CancelCustomerOrderCommandValidator : AbstractValidator<CancelCustomerOrderCommand>
{
    public CancelCustomerOrderCommandValidator()
    {
        RuleFor(value => value.SessionId).NotEmpty();
        RuleFor(value => value.Request).NotNull().SetValidator(new CustomerOrderRecoveryValidator());
    }
}
