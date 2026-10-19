using FluentValidation;
using RestaurantSystem.Api.Features.Addresses.Dtos;
namespace RestaurantSystem.Api.Features.Addresses.Validation;

public sealed class CustomerAddressWriteRequestValidator : AbstractValidator<CustomerAddressWriteRequest>
{
    public CustomerAddressWriteRequestValidator()
    {
        RuleFor(value => value.SchemaVersion).Equal(1);
        RuleFor(value => value.Address).NotNull().SetValidator(new CustomerAddressInputValidator());
    }
}
