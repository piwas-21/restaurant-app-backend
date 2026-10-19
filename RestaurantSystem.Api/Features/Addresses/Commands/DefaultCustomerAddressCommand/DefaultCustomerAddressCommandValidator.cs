using FluentValidation;
using RestaurantSystem.Api.Features.Addresses.Validation;
namespace RestaurantSystem.Api.Features.Addresses.Commands.DefaultCustomerAddressCommand;

public sealed class DefaultCustomerAddressCommandValidator : AbstractValidator<DefaultCustomerAddressCommand>
{
    public DefaultCustomerAddressCommandValidator()
    {
        RuleFor(value => value.Id).NotEmpty();
        RuleFor(value => value.Request).NotNull().SetValidator(new CustomerAddressActionRequestValidator());
    }
}
