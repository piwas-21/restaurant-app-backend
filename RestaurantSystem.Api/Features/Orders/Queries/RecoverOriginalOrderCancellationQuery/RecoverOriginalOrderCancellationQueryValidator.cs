using FluentValidation;
using RestaurantSystem.Api.Features.Orders.Commands.SubmitCustomerOrderCancellationCommand;

namespace RestaurantSystem.Api.Features.Orders.Queries.RecoverOriginalOrderCancellationQuery;

public sealed class RecoverOriginalOrderCancellationQueryValidator : AbstractValidator<RecoverOriginalOrderCancellationQuery>
{
    public RecoverOriginalOrderCancellationQueryValidator()
    {
        RuleFor(value => value.SessionId).NotEmpty();
        RuleFor(value => value.Request).NotNull().SetValidator(new CustomerOrderCancellationGuestValidator());
    }
}
