using System.Text.Json;

namespace RestaurantSystem.Channels.Api;

public interface IChannelAvailabilityStatus
{
    Task<JsonElement> Read(CancellationToken cancellationToken);
}
