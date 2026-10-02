using RestaurantSystem.Api.Features.DeliveryChannels.Dtos;

namespace RestaurantSystem.Api.Features.DeliveryChannels.Services;

public interface IChannelDecisionDelivery
{
    Task<ChannelDecisionLeaseDto?> ClaimAsync(CancellationToken cancellationToken);
    Task<ChannelDecisionDto> ReportAsync(Guid decisionId, ChannelDecisionReport report, CancellationToken cancellationToken);
}
