using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Features.DeliveryChannels.Dtos;
using RestaurantSystem.Api.Features.DeliveryChannels.Services;

namespace RestaurantSystem.Api.Features.DeliveryChannels.Commands.ClaimChannelDecisionCommand;

public sealed record ClaimChannelDecisionCommand : ICommand<ChannelDecisionLeaseDto?>;

public sealed class ClaimChannelDecisionCommandHandler(IChannelDecisionDelivery delivery)
    : ICommandHandler<ClaimChannelDecisionCommand, ChannelDecisionLeaseDto?>
{
    public Task<ChannelDecisionLeaseDto?> Handle(ClaimChannelDecisionCommand command, CancellationToken cancellationToken)
        => delivery.ClaimAsync(cancellationToken);
}
