using System.Text.Json;

namespace RestaurantSystem.Channels.Api;

public interface ITenantCataloguePublication
{
    Task<JsonElement> Preview(JsonElement template, CancellationToken cancellationToken);
    Task<JsonElement> Publish(JsonElement template, string revision, CancellationToken cancellationToken);
    Task<JsonElement> Expected(CancellationToken cancellationToken);
    Task RequireActive(CancellationToken cancellationToken);
}
