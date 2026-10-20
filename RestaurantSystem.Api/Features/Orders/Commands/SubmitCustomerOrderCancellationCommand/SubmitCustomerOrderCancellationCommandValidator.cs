using FluentValidation;

namespace RestaurantSystem.Api.Features.Orders.Commands.SubmitCustomerOrderCancellationCommand;

public sealed class SubmitCustomerOrderCancellationCommandValidator : AbstractValidator<SubmitCustomerOrderCancellationCommand>
{
    public SubmitCustomerOrderCancellationCommandValidator()
    {
        RuleFor(value => value.Intent).NotNull().SetValidator(new CustomerOrderCancellationIntentValidator());
    }
}
