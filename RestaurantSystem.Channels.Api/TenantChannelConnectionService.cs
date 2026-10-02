using System.Net.Http;
using System.Text.Json;
using RestaurantSystem.Channels.Domain;

namespace RestaurantSystem.Channels.Api;

public sealed class TenantChannelConnectionService(TenantManagementContext context, ISandboxConnection connection,
    ITenantOAuthFlows flows, IChannelManagementConnectionState connectionState,
    ITenantChannelAvailabilityState availabilityState, IChannelManagementAudit audit) : ITenantChannelConnectionService
{
    public async Task<JsonElement> Disconnect(Guid storeId, Guid actorId, CancellationToken cancellationToken)
    {
        context.RequireEnabled();
        if (storeId != context.ConfiguredStore.StoreId)
            throw new ChannelConsoleException(404, "The approved Uber store was not found.");
        var binding = context.Binding();
        await using var lease = await availabilityState.TryLease(binding, cancellationToken);
        if (lease is null) throw new ChannelConsoleException(409, "A channel operation is in progress. Reload before disconnecting.");
        var now = context.Clock.GetUtcNow();
        await audit.Record(binding, actorId, "Disconnect", "Intent", null, now, cancellationToken);
        await flows.CancelPending(binding, "ConnectionDisconnected", now, cancellationToken);
        await connectionState.Set(binding, true, actorId, now, cancellationToken);
        await availabilityState.SetOverride(binding, true, null, actorId, now, cancellationToken);
        try
        {
            var actual = await connection.EnableOrders(false, cancellationToken);
            var relinquished = ProviderRevoked(actual);
            await audit.Record(binding, actorId, "Disconnect", relinquished ? "Confirmed" : "Unconfirmed",
                null, context.Clock.GetUtcNow(), cancellationToken);
            return DisconnectResult(relinquished, context.Clock.GetUtcNow());
        }
        catch (Exception exception) when (IsProviderFailure(exception, cancellationToken))
        {
            await audit.Record(binding, actorId, "Disconnect", "Unconfirmed", null, context.Clock.GetUtcNow(), cancellationToken);
            return DisconnectResult(false, context.Clock.GetUtcNow());
        }
    }

    private static bool ProviderRevoked(JsonElement actual)
        => !ProviderJson.Flag(actual, "enabled") && !ProviderJson.Flag(actual, "orderManager") && !ProviderJson.Flag(actual, "pending");

    private static JsonElement DisconnectResult(bool relinquished, DateTimeOffset completedAt)
        => ProviderJson.Encode(new
        {
            status = relinquished ? "disconnected" : "uncertain",
            providerManagerRelinquished = relinquished,
            localBridgePaused = true,
            resultCode = relinquished ? null : "ProviderRevocationUnconfirmed",
            completedAt
        });

    private static bool IsProviderFailure(Exception exception, CancellationToken cancellationToken)
        => exception is ChannelConsoleException or HttpRequestException
            || exception is TaskCanceledException && !cancellationToken.IsCancellationRequested;
}
