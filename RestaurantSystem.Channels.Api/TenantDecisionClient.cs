using System.Text.Json;

namespace RestaurantSystem.Channels.Api;

public sealed class TenantDecisionClient(ITenantChannelTransport transport, TimeProvider clock) : ITenantDecisionClient
{
    private static readonly JsonSerializerOptions Wire = new(JsonSerializerDefaults.Web);

    public async Task<TenantDecisionLease?> Claim(TenantStoreBinding store, CancellationToken cancellationToken)
    {
        var body = await transport.Post(store, "/api/delivery-channels/decisions/claim", null, cancellationToken);
        if (body is null || body.Value.ValueKind == JsonValueKind.Null) return null;
        TenantDecisionLease lease;
        try { lease = body.Value.Deserialize<TenantDecisionLease>(Wire) ?? throw InvalidReply(); }
        catch (JsonException) { throw InvalidReply(); }
        if (lease.DecisionId == Guid.Empty || lease.LeaseId == Guid.Empty || lease.OrderId == Guid.Empty
            || lease.LeaseUntil <= clock.GetUtcNow() || lease.Provider != "uber-eats"
            || !Guid.TryParseExact(lease.StoreId, "D", out var storeId) || storeId != store.StoreId
            || !Guid.TryParseExact(lease.ExternalOrderId, "D", out var orderId) || orderId == Guid.Empty
            || lease.Action is not ("accept" or "deny") || string.IsNullOrWhiteSpace(lease.Reason)
            || lease.Reason.Length > 250 || lease.Reason.Any(char.IsControl) || lease.Attempt < 1)
            throw InvalidReply();
        return lease;
    }

    public async Task Report(TenantStoreBinding store, TenantDecisionLease lease, TenantDecisionReport report, CancellationToken cancellationToken)
    {
        var reply = await transport.Post(store, $"/api/delivery-channels/decisions/{lease.DecisionId:D}/report", report, cancellationToken);
        if (reply is not { } body || body.ValueKind != JsonValueKind.Object
            || !body.TryGetProperty("orderId", out var id) || id.ValueKind != JsonValueKind.String || !id.TryGetGuid(out var orderId) || orderId != lease.OrderId
            || !body.TryGetProperty("operationId", out var operation) || operation.ValueKind != JsonValueKind.String || !operation.TryGetGuid(out var operationId) || operationId == Guid.Empty
            || ProviderJson.Text(body, "action") != lease.Action || ProviderJson.Text(body, "state") != report.State)
            throw InvalidReply();
    }

    private static ChannelConsoleException InvalidReply() => new(502, "The tenant decision reply did not confirm the expected lease or order.");
}
