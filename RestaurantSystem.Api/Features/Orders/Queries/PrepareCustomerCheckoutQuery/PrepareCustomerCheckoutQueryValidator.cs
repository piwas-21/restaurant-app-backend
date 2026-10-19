using FluentValidation;
using RestaurantSystem.Api.Features.Orders.Commands.SubmitCustomerCheckoutCommand;

namespace RestaurantSystem.Api.Features.Orders.Queries.PrepareCustomerCheckoutQuery;

public sealed class PrepareCustomerCheckoutQueryValidator : AbstractValidator<PrepareCustomerCheckoutQuery>
{
    public PrepareCustomerCheckoutQueryValidator()
    {
        RuleFor(value => value.SessionId).NotEmpty();
        RuleFor(value => value.Request).NotNull().SetValidator(new CustomerCheckoutIntentValidator());
    }
}
