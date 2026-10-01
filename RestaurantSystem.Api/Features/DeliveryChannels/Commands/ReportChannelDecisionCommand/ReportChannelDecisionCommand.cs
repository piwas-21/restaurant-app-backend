using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Features.DeliveryChannels.Dtos;
using RestaurantSystem.Api.Features.DeliveryChannels.Services;

namespace RestaurantSystem.Api.Features.DeliveryChannels.Commands.ReportChannelDecisionCommand;

public sealed record ReportChannelDecisionCommand(Guid DecisionId, ChannelDecisionReport Report) : ICommand<ChannelDecisionDto>;

public sealed class ReportChannelDecisionCommandHandler(IChannelDecisionDelivery delivery)
    : ICommandHandler<ReportChannelDecisionCommand, ChannelDecisionDto>
{
    public Task<ChannelDecisionDto> Handle(ReportChannelDecisionCommand command, CancellationToken cancellationToken)
        => delivery.ReportAsync(command.DecisionId, command.Report, cancellationToken);
}
