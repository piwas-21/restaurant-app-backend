using FluentValidation;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Domain.Common.Constants;

namespace RestaurantSystem.Api.Features.Orders.Commands.SubmitCustomerCheckoutCommand;

public sealed class CustomerCheckoutContactValidator : AbstractValidator<CustomerCheckoutContactDto>
{
    public CustomerCheckoutContactValidator()
    {
        RuleFor(value => value.Name).NotEmpty().MaximumLength(OrderFieldLimits.CustomerNameMaxLength);
        RuleFor(value => value.Email).NotEmpty().EmailAddress().MaximumLength(OrderFieldLimits.CustomerEmailMaxLength);
        RuleFor(value => value.Phone).NotEmpty().MaximumLength(OrderFieldLimits.CustomerPhoneMaxLength);
    }
}
