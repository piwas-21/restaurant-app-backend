using System.Text.Json;

namespace RestaurantSystem.Channels.Api;

public interface ISandboxMenu
{
    JsonElement Preview();
    Task<JsonElement> Read(CancellationToken cancellationToken);
    Task<JsonElement> Publish(CancellationToken cancellationToken);
    Task RequireVerified(CancellationToken cancellationToken);
}
