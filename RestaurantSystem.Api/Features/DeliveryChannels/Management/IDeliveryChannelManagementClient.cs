namespace RestaurantSystem.Api.Features.DeliveryChannels.Management;

public interface IDeliveryChannelManagementClient
{
    Task<TResponse> Send<TResponse>(HttpMethod method, string path, Guid actorId, object? body,
        CancellationToken cancellationToken);
}
