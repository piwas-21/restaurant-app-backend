using FluentValidation;
using RestaurantSystem.Api.Features.Addresses.Validation;
namespace RestaurantSystem.Api.Features.Addresses.Commands.CreateCustomerAddressCommand;

public sealed class CreateCustomerAddressCommandValidator : AbstractValidator<CreateCustomerAddressCommand>
{
    public CreateCustomerAddressCommandValidator()
    {
        RuleFor(value => value.Request).NotNull().SetValidator(new CustomerAddressWriteRequestValidator());
    }
}
