using FluentValidation;

namespace RestaurantSystem.Api.Features.Orders.Commands.RejectDelayCommand;

public class RejectDelayCommandValidator : AbstractValidator<RejectDelayCommand>
{
    public RejectDelayCommandValidator()
    {
        RuleFor(x => x.OrderId).NotEmpty().WithMessage("Order ID is required");
    }
}
