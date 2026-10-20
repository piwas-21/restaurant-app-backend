using FluentValidation;
using RestaurantSystem.Api.Features.Orders.Commands.SubmitCustomerOrderCancellationCommand;

namespace RestaurantSystem.Api.Features.Orders.Commands.SubmitOriginalOrderCancellationCommand;

public sealed class SubmitOriginalOrderCancellationCommandValidator : AbstractValidator<SubmitOriginalOrderCancellationCommand>
{
    public SubmitOriginalOrderCancellationCommandValidator()
    {
        RuleFor(value => value.SessionId).NotEmpty();
        RuleFor(value => value.Request).NotNull().SetValidator(new CustomerOrderCancellationGuestValidator());
    }
}
