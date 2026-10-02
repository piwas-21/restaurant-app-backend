using RestaurantSystem.Api.Features.DeliveryChannels.Dtos;

namespace RestaurantSystem.Api.Features.DeliveryChannels.Services;

public interface IChannelAvailabilityReader
{
    Task<ChannelAvailabilitySnapshot> Read(ChannelAvailabilityRequest request, CancellationToken cancellationToken);
}
