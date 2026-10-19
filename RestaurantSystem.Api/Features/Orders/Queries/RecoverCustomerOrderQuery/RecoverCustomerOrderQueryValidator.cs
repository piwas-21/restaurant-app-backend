using FluentValidation;
using RestaurantSystem.Api.Features.Orders.Commands.SubmitCustomerCheckoutCommand;

namespace RestaurantSystem.Api.Features.Orders.Queries.RecoverCustomerOrderQuery;

public sealed class RecoverCustomerOrderQueryValidator : AbstractValidator<RecoverCustomerOrderQuery>
{
    public RecoverCustomerOrderQueryValidator()
    {
        RuleFor(value => value.SessionId).NotEmpty();
        RuleFor(value => value.Request).NotNull().SetValidator(new CustomerOrderRecoveryValidator());
    }
}
