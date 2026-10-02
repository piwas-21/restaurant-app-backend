using System.Text.Json;

namespace RestaurantSystem.Channels.Api;

public interface ISandboxConnection
{
    Task<string> Start(string sessionHash, CancellationToken cancellationToken, bool enableTesting = false);
    Task Complete(string sessionHash, string state, string code, string error, CancellationToken cancellationToken);
    Task<JsonElement> ConnectTenant(string merchantToken, CancellationToken cancellationToken);
    Task<JsonElement> ConnectTenant(string merchantToken, bool enableOrderAcceptance, CancellationToken cancellationToken);
    Task<JsonElement> Configuration(CancellationToken cancellationToken);
    Task<JsonElement> EnableOrders(bool enable, CancellationToken cancellationToken);
}
