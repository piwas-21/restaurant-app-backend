using FluentValidation;
using RestaurantSystem.Api.Features.Addresses.Dtos;
namespace RestaurantSystem.Api.Features.Addresses.Validation;

public sealed class CustomerAddressActionRequestValidator : AbstractValidator<CustomerAddressActionRequest>
{
    public CustomerAddressActionRequestValidator() => RuleFor(value => value.SchemaVersion).Equal(1);
}
