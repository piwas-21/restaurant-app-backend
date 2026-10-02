using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using RestaurantSystem.Channels.Domain;

namespace RestaurantSystem.Channels.Api;

public sealed class CreatedOrderRecovery(ISandboxTokens tokens, IUberSandboxClient provider, ICreatedOrderDiscoveries discoveries,
    TimeProvider clock, ILogger<CreatedOrderRecovery> logger, ITenantChannelOrderProcessingPolicy policy) : ICreatedOrderRecovery
{
    public async Task Process(CancellationToken cancellationToken)
    {
        var context = await policy.ResolveRecovery(cancellationToken);
        if (context is null) return;
        var path = $"/v1/eats/stores/{context.Store.StoreId:D}/created-orders?limit={context.ListLimit.ToString(CultureInfo.InvariantCulture)}";
        var response = await provider.Send(HttpMethod.Get, path, await tokens.CreatedOrdersToken(cancellationToken), null, cancellationToken);
        ProviderJson.RequireSuccess(response, "created-order recovery");
        if (response.ClientId.Length > 0 && response.ClientId != context.ClientId)
            throw new ChannelConsoleException(502, "Uber returned created-order evidence for a different client.");
        if (response.Body.ValueKind != JsonValueKind.Object || !response.Body.TryGetProperty("orders", out var rows) || rows.ValueKind != JsonValueKind.Array
            || rows.GetArrayLength() > context.ListLimit)
            throw new ChannelConsoleException(502, "Uber's created-order list is malformed or exceeds the recovery bound; review store backlog.");
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(response.Body.GetRawText())));
        if (rows.GetArrayLength() == context.ListLimit)
            logger.LogWarning("Sandbox created-order recovery reached its list bound; review the store backlog.");
        var candidates = ParseCandidates(rows, clock.GetUtcNow(), context.EnrollmentStartedAt, hash);
        await discoveries.Record(context.ClientId, context.Store.StoreId, context.Store.TenantId, context.Store.CatalogueRevision,
            context.EnrollmentStartedAt, candidates, cancellationToken);
    }

    private static CreatedOrderCandidate[] ParseCandidates(JsonElement rows, DateTimeOffset now, DateTimeOffset enrolledAt, string hash)
    {
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
            if (placedAt >= enrolledAt) candidates.Add(new(id, placedAt, hash));
        }
        // Validate the entire batch before persisting any candidate. Canonical GET still verifies order/store.
        return candidates.OrderBy(order => order.PlacedAt).ThenBy(order => order.OrderId).DistinctBy(order => order.OrderId).ToArray();
    }
}
