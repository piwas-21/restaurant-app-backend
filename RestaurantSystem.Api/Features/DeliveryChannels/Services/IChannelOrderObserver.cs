using RestaurantSystem.Api.Features.DeliveryChannels.Dtos;

namespace RestaurantSystem.Api.Features.DeliveryChannels.Services;

public interface IChannelOrderObserver
{
    Task<ChannelOrderObservationDto> ObserveAsync(Guid orderId, ChannelOrderObservation observation, CancellationToken cancellationToken);
}
