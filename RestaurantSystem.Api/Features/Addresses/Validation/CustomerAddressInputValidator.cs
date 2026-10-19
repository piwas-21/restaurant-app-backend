using FluentValidation;
using RestaurantSystem.Api.Features.Addresses.Dtos;
namespace RestaurantSystem.Api.Features.Addresses.Validation;

public sealed class CustomerAddressInputValidator : AddressContentValidator<CustomerAddressInput>
{
    public CustomerAddressInputValidator() => RuleFor(value => value.IsDefault).NotNull();
}
