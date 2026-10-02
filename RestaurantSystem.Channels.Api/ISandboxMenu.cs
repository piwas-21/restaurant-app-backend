using System.Text.Json;

namespace RestaurantSystem.Channels.Api;

public interface ISandboxMenu
{
    JsonElement Preview();
    Task<JsonElement> Preview(CancellationToken cancellationToken) => Task.FromResult(Preview());
    Task<JsonElement> Read(CancellationToken cancellationToken);
    Task<JsonElement> Publish(CancellationToken cancellationToken);
    Task<JsonElement> Publish(string revision, CancellationToken cancellationToken) => Publish(cancellationToken);
    Task RequireVerified(CancellationToken cancellationToken);
    Task<JsonElement> Expected(CancellationToken cancellationToken) => Task.FromResult(Preview());
}
