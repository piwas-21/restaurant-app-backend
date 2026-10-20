using FluentValidation;
using RestaurantSystem.Api.Features.Addresses.Validation;
namespace RestaurantSystem.Api.Features.Addresses.Commands.UpdateCustomerAddressCommand;

public sealed class UpdateCustomerAddressCommandValidator : AbstractValidator<UpdateCustomerAddressCommand>
{
    public UpdateCustomerAddressCommandValidator()
    {
        RuleFor(value => value.Id).NotEmpty();
        RuleFor(value => value.Request).NotNull().SetValidator(new CustomerAddressWriteRequestValidator());
    }
}
