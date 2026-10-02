using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using RestaurantSystem.Channels.Domain;

namespace RestaurantSystem.Channels.Api;

public sealed class CreatedOrderRecovery(IOptions<TenantBridgeSettings> settings, IOptions<UberWebhookSettings> webhook,
    ISandboxTokens tokens, IUberSandboxClient provider, ICreatedOrderDiscoveries discoveries, TimeProvider clock, ILogger<CreatedOrderRecovery> logger) : ICreatedOrderRecovery
{
    public async Task Process(CancellationToken cancellationToken)
    {
        var options = settings.Value;
        if (!options.Enabled || options.Paused || !options.RecoverCreatedOrders) return;
        var store = options.Store;
        if (webhook.Value.StoreIds.Length != 1 || webhook.Value.StoreIds[0] != store.StoreId)
            throw new ChannelConsoleException(409, "Recovery requires the approved sandbox store binding.");
        var path = $"/v1/eats/stores/{store.StoreId:D}/created-orders?limit={options.RecoveryListLimit.ToString(CultureInfo.InvariantCulture)}";
        var response = await provider.Send(HttpMethod.Get, path, await tokens.CreatedOrdersToken(cancellationToken), null, cancellationToken);
        ProviderJson.RequireSuccess(response, "created-order recovery");
        if (response.ClientId.Length > 0 && response.ClientId != webhook.Value.ClientId)
            throw new ChannelConsoleException(502, "Uber returned created-order evidence for a different client.");
        if (response.Body.ValueKind != JsonValueKind.Object || !response.Body.TryGetProperty("orders", out var rows) || rows.ValueKind != JsonValueKind.Array
            || rows.GetArrayLength() > options.RecoveryListLimit)
            throw new ChannelConsoleException(502, "Uber's created-order list is malformed or exceeds the recovery bound; review store backlog.");
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(response.Body.GetRawText())));
        if (rows.GetArrayLength() == options.RecoveryListLimit)
            logger.LogWarning("Sandbox created-order recovery reached its list bound; review the store backlog.");
        var now = clock.GetUtcNow();
        var candidates = new List<CreatedOrderCandidate>();
        var identities = new Dictionary<Guid, DateTimeOffset>();
        foreach (var row in rows.EnumerateArray())
        {
            if (row.ValueKind != JsonValueKind.Object || !Guid.TryParseExact(ProviderJson.Text(row, "id"), "D", out var id)
                || id == Guid.Empty || ProviderJson.Text(row, "current_state") != "CREATED"
                || !ProviderOrderTimestamp.TryRead(ProviderJson.Text(row, "placed_at"), now, out var placedAt))
                throw new ChannelConsoleException(502, "Uber returned an invalid created-order identity or timestamp.");
            if (identities.TryGetValue(id, out var previous) && previous != placedAt)
                throw new ChannelConsoleException(502, "Uber returned conflicting created-order timestamps.");
            identities[id] = placedAt;
            if (placedAt >= options.EnrollmentStartedAt) candidates.Add(new(id, placedAt, hash));
        }
        // Validate the entire batch before persisting any candidate. Canonical GET still verifies order/store.
        await discoveries.Record(webhook.Value.ClientId, store.StoreId, store.TenantId, store.CatalogueRevision, options.EnrollmentStartedAt,
            candidates.OrderBy(order => order.PlacedAt).ThenBy(order => order.OrderId).DistinctBy(order => order.OrderId).ToArray(), cancellationToken);
    }
}
