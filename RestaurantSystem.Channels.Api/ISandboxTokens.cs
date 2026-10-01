using System.Text.Json;

namespace RestaurantSystem.Channels.Api;

public interface ISandboxTokens
{
    Task<string> AppToken(CancellationToken cancellationToken);
    Task<string> Exchange(string code, string verifier, CancellationToken cancellationToken);
}
