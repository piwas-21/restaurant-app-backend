using System.Text.Json;

namespace RestaurantSystem.Channels.Api;

public sealed class TenantOrderClient(ITenantChannelTransport transport) : ITenantOrderClient
{
    public async Task<TenantImportResult> Import(TenantStoreBinding store, TenantOrderRequest order, CancellationToken cancellationToken)
    {
        var reply = await transport.Post(store, "/api/delivery-channels/orders", order, cancellationToken);
        if (reply is not { } body || body.ValueKind != JsonValueKind.Object
            || !body.TryGetProperty("orderId", out var id) || id.ValueKind != JsonValueKind.String
            || !id.TryGetGuid(out var orderId) || orderId == Guid.Empty
            || !body.TryGetProperty("alreadyImported", out var replay) || replay.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            throw new ChannelConsoleException(502, "The tenant import reply did not confirm a durable order identity.");
        return new(orderId, replay.GetBoolean());
    }
}
