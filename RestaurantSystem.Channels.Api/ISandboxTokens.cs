using System.Text.Json;

namespace RestaurantSystem.Channels.Api;

public interface ISandboxTokens
{
    Task<string> CreatedOrdersToken(CancellationToken cancellationToken);
    Task<string> AppToken(CancellationToken cancellationToken);
    Task<string> Exchange(string code, string verifier, CancellationToken cancellationToken);
    Task<string> Exchange(string code, string verifier, string redirectUri, CancellationToken cancellationToken);
}
