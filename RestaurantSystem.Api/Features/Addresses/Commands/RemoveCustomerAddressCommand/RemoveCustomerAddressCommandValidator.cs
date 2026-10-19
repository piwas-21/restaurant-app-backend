using FluentValidation;
using RestaurantSystem.Api.Features.Addresses.Validation;
namespace RestaurantSystem.Api.Features.Addresses.Commands.RemoveCustomerAddressCommand;

public sealed class RemoveCustomerAddressCommandValidator : AbstractValidator<RemoveCustomerAddressCommand>
{
    public RemoveCustomerAddressCommandValidator()
    {
        RuleFor(value => value.Id).NotEmpty();
        RuleFor(value => value.Request).NotNull().SetValidator(new CustomerAddressActionRequestValidator());
    }
}
