using FluentValidation;

namespace RestaurantSystem.Api.Features.Orders.Commands.DeleteOrderCommand;

public class DeleteOrderCommandValidator : AbstractValidator<DeleteOrderCommand>
{
    public DeleteOrderCommandValidator()
    {
        RuleFor(x => x.OrderId).NotEmpty().WithMessage("Order ID is required");
    }
}
