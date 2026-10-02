using System.Text.Json;
using Microsoft.Extensions.Options;
using RestaurantSystem.Channels.Domain;

namespace RestaurantSystem.Channels.Api;

public sealed class TenantDecisionProcessor(IOptions<TenantBridgeSettings> settings, IOptions<UberWebhookSettings> webhook,
    IChannelOrderLinks links, IConsoleRepository repository, ISandboxOrders orders, ITenantDecisionClient tenant,
    TimeProvider clock) : ITenantDecisionProcessor
{
    public async Task<bool> Process(CancellationToken cancellationToken)
    {
        var options = settings.Value;
        if (!options.Enabled || options.Paused || !options.DispatchDecisions) return false;
        var store = options.Store;
        var lease = await tenant.Claim(store, cancellationToken);
        if (lease is null) return false;
        var orderId = Guid.ParseExact(lease.ExternalOrderId, "D");
        if (!await links.IsImported(webhook.Value.ClientId, store.StoreId, store.TenantId, orderId, lease.OrderId, cancellationToken))
        {
            await tenant.Report(store, lease, Unknown(lease), cancellationToken);
            return true;
        }
        TenantDecisionReport report;
        try
        {
            var canonical = await orders.Read(lease.ExternalOrderId, cancellationToken);
            if (ProviderJson.Text(canonical, "current_state") == "CREATED")
            {
                var sent = await repository.FindAction(webhook.Value.ClientId, store.StoreId, lease.ExternalOrderId, cancellationToken);
                // A durable uncertain/acknowledged provider action is never sent twice, even after a lost tenant report.
                if (sent is null or { State: "Failed" })
                {
                    await orders.Decide(lease.ExternalOrderId, lease.Action, lease.Reason, cancellationToken);
                    canonical = await orders.Read(lease.ExternalOrderId, cancellationToken);
                }
            }
            report = Observe(lease, canonical);
        }
        catch (ChannelConsoleException) { report = Unknown(lease); }
        catch (HttpRequestException) { report = Unknown(lease); }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { report = Unknown(lease); }
        // No successful POST/204 can release an order without this independent canonical GET.
        await tenant.Report(store, lease, report, cancellationToken);
        return true;
    }

    private TenantDecisionReport Observe(TenantDecisionLease lease, JsonElement canonical)
    {
        if (!Guid.TryParse(ProviderJson.Text(canonical, "id"), out var id) || id.ToString("D") != lease.ExternalOrderId
            || !canonical.TryGetProperty("store", out var store)
            || ProviderJson.Text(store, "id") != lease.StoreId) return Unknown(lease);
        var state = ProviderJson.Text(canonical, "current_state");
        if (lease.Action == "accept" && state is "ACCEPTED" or "FINISHED" || lease.Action == "deny" && state == "DENIED")
            return new(lease.LeaseId, "Succeeded", state, CanonicalHash(canonical), clock.GetUtcNow());
        if (state == "CANCELED") return new(lease.LeaseId, "Failed", state, CanonicalHash(canonical), clock.GetUtcNow());
        if (state == "CREATED") return new(lease.LeaseId, "Unknown", state, CanonicalHash(canonical), clock.GetUtcNow());
        // Conflicting/unrecognized evidence stays on hold for canonical reconciliation, never guessed into success.
        return Unknown(lease);
    }

    private TenantDecisionReport Unknown(TenantDecisionLease lease, string state = "Unknown")
        => new(lease.LeaseId, state, "UNKNOWN", string.Empty, clock.GetUtcNow());

    private static string CanonicalHash(JsonElement body)
        => Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(body)));
}
