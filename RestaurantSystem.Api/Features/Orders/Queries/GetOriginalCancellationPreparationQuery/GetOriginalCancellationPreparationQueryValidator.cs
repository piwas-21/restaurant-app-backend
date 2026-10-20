using FluentValidation;
using RestaurantSystem.Api.Features.Orders.Commands.SubmitCustomerCheckoutCommand;

namespace RestaurantSystem.Api.Features.Orders.Queries.GetOriginalCancellationPreparationQuery;

public sealed class GetOriginalCancellationPreparationQueryValidator : AbstractValidator<GetOriginalCancellationPreparationQuery>
{
    public GetOriginalCancellationPreparationQueryValidator()
    {
        RuleFor(value => value.SessionId).NotEmpty();
        RuleFor(value => value.Proof).NotNull().SetValidator(new CustomerOrderRecoveryValidator());
    }
}
