using RestaurantSystem.Api.Features.Devices.Dtos;

namespace RestaurantSystem.Api.Features.Orders.Services;

public interface IOrderRoutingAcknowledgementService
{
    Task ApplyAcknowledgementAsync(
        string deviceId, PrintAckDto acknowledgement, CancellationToken cancellationToken);
}
