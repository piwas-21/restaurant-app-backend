using FluentValidation;

namespace RestaurantSystem.Api.Features.DeliveryChannels.Commands.ObserveChannelOrderCommand;

public sealed class ObserveChannelOrderCommandValidator : AbstractValidator<ObserveChannelOrderCommand>
{
    public ObserveChannelOrderCommandValidator()
    {
        RuleFor(command => command.OrderId).NotEmpty();
        RuleFor(command => command.Observation.Provider).Equal("uber-eats");
        RuleFor(command => command.Observation.StoreId).NotEmpty().MaximumLength(200);
        RuleFor(command => command.Observation.ExternalOrderId).NotEmpty().MaximumLength(200);
        RuleFor(command => command.Observation.CanonicalState)
            .Must(state => state is "CREATED" or "ACCEPTED" or "DENIED" or "CANCELED" or "FINISHED");
        RuleFor(command => command.Observation.CanonicalHash).NotEmpty().Matches("^[a-f0-9]{64}$");
        RuleFor(command => command.Observation.ObservedAt).NotEmpty();
    }
}
