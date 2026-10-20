using FluentValidation;
using RestaurantSystem.Api.Features.Orders.Commands.SubmitCustomerOrderCancellationCommand;

namespace RestaurantSystem.Api.Features.Orders.Queries.RecoverCustomerOrderCancellationQuery;

public sealed class RecoverCustomerOrderCancellationQueryValidator : AbstractValidator<RecoverCustomerOrderCancellationQuery>
{
    public RecoverCustomerOrderCancellationQueryValidator()
    {
        RuleFor(value => value.Intent).NotNull().SetValidator(new CustomerOrderCancellationIntentValidator());
    }
}
