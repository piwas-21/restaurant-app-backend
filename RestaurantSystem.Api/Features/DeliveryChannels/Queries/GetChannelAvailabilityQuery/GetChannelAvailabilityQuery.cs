using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Features.DeliveryChannels.Dtos;
using RestaurantSystem.Api.Features.DeliveryChannels.Services;

namespace RestaurantSystem.Api.Features.DeliveryChannels.Queries.GetChannelAvailabilityQuery;

public sealed record GetChannelAvailabilityQuery(ChannelAvailabilityRequest Request) : IQuery<ChannelAvailabilitySnapshot>;

public sealed class GetChannelAvailabilityQueryHandler(IChannelAvailabilityReader reader)
    : IQueryHandler<GetChannelAvailabilityQuery, ChannelAvailabilitySnapshot>
{
    public Task<ChannelAvailabilitySnapshot> Handle(GetChannelAvailabilityQuery query, CancellationToken cancellationToken)
        => reader.Read(query.Request, cancellationToken);
}
