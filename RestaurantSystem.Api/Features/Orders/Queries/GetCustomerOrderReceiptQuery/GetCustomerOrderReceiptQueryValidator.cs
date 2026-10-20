using FluentValidation;
using RestaurantSystem.Api.Features.Orders.Commands.SubmitCustomerCheckoutCommand;

namespace RestaurantSystem.Api.Features.Orders.Queries.GetCustomerOrderReceiptQuery;

public sealed class GetCustomerOrderReceiptQueryValidator : AbstractValidator<GetCustomerOrderReceiptQuery>
{
    public GetCustomerOrderReceiptQueryValidator()
    {
        RuleFor(value => value.SessionId).NotEmpty();
        RuleFor(value => value.Proof).NotNull().SetValidator(new CustomerOrderRecoveryValidator());
    }
}
