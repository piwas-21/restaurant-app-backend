using System.Text.Json;

namespace RestaurantSystem.Channels.Api;

public interface ITenantCataloguePublication
{
    Task<JsonElement> Preview(JsonElement template, CancellationToken cancellationToken);
    Task<JsonElement> Preview(JsonElement template, TenantStoreBinding store, CancellationToken cancellationToken);
    Task<JsonElement> Publish(JsonElement template, string revision, CancellationToken cancellationToken);
    Task<JsonElement> Publish(JsonElement template, string revision, TenantStoreBinding store, CancellationToken cancellationToken,
        Func<CancellationToken, Task<bool>>? intentStillCurrent = null);
    Task<JsonElement> Expected(CancellationToken cancellationToken);
    Task RequireActive(CancellationToken cancellationToken);
}
