using System.Text.Json;

namespace RestaurantSystem.Channels.Api;

public sealed class TenantObservationClient(ITenantChannelTransport transport) : ITenantObservationClient
{
    public async Task<bool> Observe(TenantStoreBinding store, Guid tenantOrderId, TenantOrderObservation observation, CancellationToken cancellationToken)
    {
        var reply = await transport.Post(store, $"/api/delivery-channels/orders/{tenantOrderId:D}/observe", observation, cancellationToken);
        if (reply is not { } body || body.ValueKind != JsonValueKind.Object
            || !body.TryGetProperty("orderId", out var id) || id.ValueKind != JsonValueKind.String
            || !id.TryGetGuid(out var orderId) || orderId != tenantOrderId
            || ProviderJson.Text(body, "canonicalState") != observation.CanonicalState
            || ProviderJson.OptionalFlag(body, "isTerminal") is not { } terminal
            || terminal != (observation.CanonicalState is "CANCELED" or "DENIED" or "FINISHED"))
            throw new ChannelConsoleException(502, "The tenant did not confirm the expected marketplace observation.");
        return terminal;
    }
}
