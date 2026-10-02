using RestaurantSystem.Api.Features.DeliveryChannels.Dtos;

namespace RestaurantSystem.Api.Features.DeliveryChannels.Services;

public interface IChannelDecisionQueue
{
    Task<ChannelDecisionDto> QueueAsync(Guid orderId, ChannelDecisionRequest request, CancellationToken cancellationToken);
    Task<ChannelDecisionDto?> ReadAsync(Guid orderId, CancellationToken cancellationToken);
}
