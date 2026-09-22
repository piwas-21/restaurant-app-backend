using FluentValidation;

namespace RestaurantSystem.Api.Features.ServerWorkspace.Commands.DeliverServerTaskCommand;

public sealed class DeliverServerTaskCommandValidator : AbstractValidator<DeliverServerTaskCommand>
{
    public DeliverServerTaskCommandValidator()
    {
        RuleFor(command => command.OrderId).NotEmpty();
        RuleFor(command => command.ExpectedVersion).GreaterThan(0);
    }
}
