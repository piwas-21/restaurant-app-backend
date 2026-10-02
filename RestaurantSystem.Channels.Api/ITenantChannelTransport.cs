using System.Text.Json;

namespace RestaurantSystem.Channels.Api;

public interface ITenantChannelTransport
{
    Task<JsonElement?> Post(TenantStoreBinding store, string path, object? body, CancellationToken cancellationToken);
}
