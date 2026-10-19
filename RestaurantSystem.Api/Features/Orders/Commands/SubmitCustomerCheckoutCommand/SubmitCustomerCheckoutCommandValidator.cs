using FluentValidation;
using RestaurantSystem.Api.Features.Orders.Commands.SubmitCustomerCheckoutCommand;

namespace RestaurantSystem.Api.Features.Orders.Commands.SubmitCustomerCheckoutCommand;

public sealed class SubmitCustomerCheckoutCommandValidator : AbstractValidator<SubmitCustomerCheckoutCommand>
{
    public SubmitCustomerCheckoutCommandValidator()
    {
        RuleFor(value => value.SessionId).NotEmpty();
        RuleFor(value => value.Request).NotNull().SetValidator(new CustomerCheckoutSubmissionValidator());
    }
}
