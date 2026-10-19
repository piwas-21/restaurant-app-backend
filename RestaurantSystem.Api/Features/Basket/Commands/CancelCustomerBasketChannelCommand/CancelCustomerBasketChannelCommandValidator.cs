using FluentValidation;

namespace RestaurantSystem.Api.Features.Basket.Commands.CancelCustomerBasketChannelCommand;

public sealed class CancelCustomerBasketChannelCommandValidator : AbstractValidator<CancelCustomerBasketChannelCommand>
{
    public CancelCustomerBasketChannelCommandValidator()
    {
        RuleFor(command => command.SessionId).NotEmpty();
        RuleFor(command => command.Request.OperationId).NotEmpty();
    }
}
