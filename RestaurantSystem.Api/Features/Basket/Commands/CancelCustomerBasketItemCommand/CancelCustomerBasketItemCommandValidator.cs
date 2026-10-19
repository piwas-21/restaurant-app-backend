using FluentValidation;

namespace RestaurantSystem.Api.Features.Basket.Commands.CancelCustomerBasketItemCommand;

public sealed class CancelCustomerBasketItemCommandValidator : AbstractValidator<CancelCustomerBasketItemCommand>
{
    public CancelCustomerBasketItemCommandValidator()
    {
        RuleFor(command => command.SessionId).NotEmpty();
        RuleFor(command => command.BasketItemId).NotEmpty();
        RuleFor(command => command.Request.OperationId).NotEmpty();
    }
}
