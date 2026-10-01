using System.Text.Json;

namespace RestaurantSystem.Channels.Api;

public interface ISandboxOrders
{
    Task<JsonElement> Receipts(CancellationToken cancellationToken);
    Task<JsonElement> Read(string orderId, CancellationToken cancellationToken);
    Task<JsonElement> Decide(string orderId, string action, string reason, CancellationToken cancellationToken);
}
