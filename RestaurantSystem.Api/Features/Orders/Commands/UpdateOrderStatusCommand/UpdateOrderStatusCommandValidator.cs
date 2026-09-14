using FluentValidation;

namespace RestaurantSystem.Api.Features.Orders.Commands.UpdateOrderStatusCommand;

public class UpdateOrderStatusCommandValidator : AbstractValidator<UpdateOrderStatusCommand>
{
    public UpdateOrderStatusCommandValidator()
    {
        RuleFor(x => x.ExpectedVersion)
            .GreaterThan(0)
            .When(x => x.ExpectedVersion.HasValue)
            .WithMessage("Expected version must be greater than zero");

        RuleFor(x => x.OrderId)
            .NotEmpty()
            .WithMessage("Order ID is required");

        RuleFor(x => x.NewStatus)
            .IsInEnum()
            .WithMessage("Invalid order status");
    }
}
