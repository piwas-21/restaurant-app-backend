using RestaurantSystem.Api.Features.DeliveryChannels.Dtos;

namespace RestaurantSystem.Api.Features.DeliveryChannels.Services;

public interface IChannelCatalogueReader
{
    Task<ChannelCatalogueSnapshot> Read(ChannelCatalogueRequest request, CancellationToken cancellationToken);
}
