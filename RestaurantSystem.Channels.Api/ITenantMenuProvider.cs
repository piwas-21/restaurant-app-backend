using System.Text.Json;

namespace RestaurantSystem.Channels.Api;

public interface ITenantMenuProvider
{
    Task<JsonElement> Read(CancellationToken cancellationToken);
    Task Upload(JsonElement menu, CancellationToken cancellationToken);
}
