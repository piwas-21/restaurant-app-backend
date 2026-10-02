using FluentValidation;

namespace RestaurantSystem.Api.Features.DeliveryChannels.Commands.QueueChannelDecisionCommand;

public sealed class QueueChannelDecisionCommandValidator : AbstractValidator<QueueChannelDecisionCommand>
{
    public QueueChannelDecisionCommandValidator()
    {
        RuleFor(command => command.OrderId).NotEmpty();
        RuleFor(command => command.Request.OperationId).NotEmpty();
        RuleFor(command => command.Request.ExpectedVersion).GreaterThan(0);
        RuleFor(command => command.Request.Action).Must(action => action is "accept" or "deny");
        RuleFor(command => command.Request.Reason).NotEmpty().MaximumLength(250)
            .Must(reason => !string.IsNullOrWhiteSpace(reason) && !reason.Any(char.IsControl));
    }
}
