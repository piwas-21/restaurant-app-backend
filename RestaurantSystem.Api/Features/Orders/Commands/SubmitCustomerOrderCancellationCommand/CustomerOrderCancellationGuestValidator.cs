using FluentValidation;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Features.Orders.Commands.SubmitCustomerCheckoutCommand;

namespace RestaurantSystem.Api.Features.Orders.Commands.SubmitCustomerOrderCancellationCommand;

public sealed class CustomerOrderCancellationGuestValidator : AbstractValidator<CustomerOrderCancellationGuestDto>
{
    public CustomerOrderCancellationGuestValidator()
    {
        RuleFor(value => value.Proof).NotNull().SetValidator(new CustomerOrderRecoveryValidator());
        RuleFor(value => value.Intent).NotNull().SetValidator(new CustomerOrderCancellationIntentValidator());
    }
}
