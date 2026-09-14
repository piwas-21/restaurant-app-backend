using FluentValidation;

namespace RestaurantSystem.Api.Features.Orders.Commands.ReleaseStaffCounterOrderCommand;

public sealed class ReleaseStaffCounterOrderCommandValidator : AbstractValidator<ReleaseStaffCounterOrderCommand>
{
    public ReleaseStaffCounterOrderCommandValidator()
    {
        RuleFor(command => command.OrderId).NotEmpty();
        RuleFor(command => command.ClientOperationId).NotEmpty();
        RuleFor(command => command.ExpectedVersion).GreaterThan(0);
    }
}
