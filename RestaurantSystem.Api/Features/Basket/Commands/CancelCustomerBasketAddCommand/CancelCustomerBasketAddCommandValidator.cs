using FluentValidation;

namespace RestaurantSystem.Api.Features.Basket.Commands.CancelCustomerBasketAddCommand;

// Cancellation never revalidates current availability, rollout or quantity settings: even a
// refused original request needs a durable seal before its client may discard the identity.
public sealed class CancelCustomerBasketAddCommandValidator : AbstractValidator<CancelCustomerBasketAddCommand>
{
    public CancelCustomerBasketAddCommandValidator()
    {
        RuleFor(command => command.SessionId).NotEmpty();
        RuleFor(command => command.Request.OperationId).NotNull().NotEqual(Guid.Empty);
    }
}
