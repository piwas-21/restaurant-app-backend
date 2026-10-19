using FluentValidation;
using RestaurantSystem.Api.Features.Orders.Dtos;

namespace RestaurantSystem.Api.Features.Orders.Commands.SubmitCustomerCheckoutCommand;

public sealed class CustomerCheckoutAddressValidator : AbstractValidator<CustomerCheckoutAddressDto>
{
    public CustomerCheckoutAddressValidator()
    {
        // Existing order_addresses storage limits; country is optional in the tenant form registry.
        RuleFor(value => value.Street).NotEmpty().MaximumLength(200);
        RuleFor(value => value.City).NotEmpty().MaximumLength(100);
        RuleFor(value => value.PostalCode).NotEmpty().MaximumLength(20);
        RuleFor(value => value.Country).NotNull().MaximumLength(100);
        RuleFor(value => value.AdditionalInfo).MaximumLength(500);
    }
}
