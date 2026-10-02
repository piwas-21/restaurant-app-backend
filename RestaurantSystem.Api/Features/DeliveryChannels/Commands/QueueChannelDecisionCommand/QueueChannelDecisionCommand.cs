using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Features.DeliveryChannels.Dtos;
using RestaurantSystem.Api.Features.DeliveryChannels.Services;

namespace RestaurantSystem.Api.Features.DeliveryChannels.Commands.QueueChannelDecisionCommand;

public sealed record QueueChannelDecisionCommand(Guid OrderId, ChannelDecisionRequest Request) : ICommand<ChannelDecisionDto>;

public sealed class QueueChannelDecisionCommandHandler(IChannelDecisionQueue queue)
    : ICommandHandler<QueueChannelDecisionCommand, ChannelDecisionDto>
{
    public Task<ChannelDecisionDto> Handle(QueueChannelDecisionCommand command, CancellationToken cancellationToken)
        => queue.QueueAsync(command.OrderId, command.Request, cancellationToken);
}
