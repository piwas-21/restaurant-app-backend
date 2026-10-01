using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Features.DeliveryChannels.Dtos;
using RestaurantSystem.Api.Features.DeliveryChannels.Services;

namespace RestaurantSystem.Api.Features.DeliveryChannels.Queries.GetChannelDecisionQuery;

public sealed record GetChannelDecisionQuery(Guid OrderId) : IQuery<ChannelDecisionDto?>;

public sealed class GetChannelDecisionQueryHandler(IChannelDecisionQueue queue)
    : IQueryHandler<GetChannelDecisionQuery, ChannelDecisionDto?>
{
    public Task<ChannelDecisionDto?> Handle(GetChannelDecisionQuery query, CancellationToken cancellationToken)
        => queue.ReadAsync(query.OrderId, cancellationToken);
}
