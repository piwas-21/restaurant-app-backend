using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Features.DeliveryChannels.Dtos;
using RestaurantSystem.Api.Features.DeliveryChannels.Services;

namespace RestaurantSystem.Api.Features.DeliveryChannels.Commands.ObserveChannelOrderCommand;

public sealed record ObserveChannelOrderCommand(Guid OrderId, ChannelOrderObservation Observation) : ICommand<ChannelOrderObservationDto>;

public sealed class ObserveChannelOrderCommandHandler(IChannelOrderObserver observer)
    : ICommandHandler<ObserveChannelOrderCommand, ChannelOrderObservationDto>
{
    public Task<ChannelOrderObservationDto> Handle(ObserveChannelOrderCommand command, CancellationToken cancellationToken)
        => observer.ObserveAsync(command.OrderId, command.Observation, cancellationToken);
}
