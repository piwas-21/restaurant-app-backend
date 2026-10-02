using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Features.DeliveryChannels.Dtos;
using RestaurantSystem.Api.Features.DeliveryChannels.Services;

namespace RestaurantSystem.Api.Features.DeliveryChannels.Queries.GetChannelCatalogueQuery;

public sealed record GetChannelCatalogueQuery(ChannelCatalogueRequest Request) : IQuery<ChannelCatalogueSnapshot>;

public sealed class GetChannelCatalogueQueryHandler(IChannelCatalogueReader reader)
    : IQueryHandler<GetChannelCatalogueQuery, ChannelCatalogueSnapshot>
{
    public Task<ChannelCatalogueSnapshot> Handle(GetChannelCatalogueQuery query, CancellationToken cancellationToken)
        => reader.Read(query.Request, cancellationToken);
}
