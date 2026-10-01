using System.Text.Json;

namespace RestaurantSystem.Channels.Api;

public interface IUberSandboxClient
{
    Task<ProviderReply> Token(IReadOnlyDictionary<string, string> fields, CancellationToken cancellationToken);
    Task<ProviderReply> Send(HttpMethod method, string path, string token, JsonElement? body, CancellationToken cancellationToken);
}
