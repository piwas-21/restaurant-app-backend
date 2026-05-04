using FluentValidation;

namespace RestaurantSystem.Api.Features.Orders.Commands.ApproveDelayCommand;

public class ApproveDelayCommandValidator : AbstractValidator<ApproveDelayCommand>
{
    public ApproveDelayCommandValidator()
    {
        RuleFor(x => x.OrderId).NotEmpty().WithMessage("Order ID is required");
    }
}
