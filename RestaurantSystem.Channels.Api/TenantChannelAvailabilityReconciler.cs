using RestaurantSystem.Channels.Domain;

namespace RestaurantSystem.Channels.Api;

public interface ITenantChannelAvailabilityReconciler
{
    Task<System.Text.Json.JsonElement> Reconcile(ChannelAvailabilityState state, Guid actorId,
        CancellationToken cancellationToken);
}

public sealed class TenantChannelAvailabilityReconciler(TenantManagementContext context,
    ITenantChannelAvailabilityState availabilityState, ITenantAvailabilityClient tenantAvailability,
    IUberAvailabilityClient provider, IChannelManagementAudit audit) : ITenantChannelAvailabilityReconciler
{
    private const string Verified = "Verified";

    public async Task<System.Text.Json.JsonElement> Reconcile(ChannelAvailabilityState state, Guid actorId,
        CancellationToken cancellationToken)
    {
        var store = await availabilityState.Active(cancellationToken);
        var binding = context.Binding(store);
        var source = await tenantAvailability.Read(store, cancellationToken);
        var intent = await availabilityState.ReadOverride(binding, cancellationToken);
        var paused = IsPaused(intent, context.Clock.GetUtcNow());
        if (CurrentRevision(source.Revision, intent, paused) != state.SourceRevision)
            return ChannelManagementJson.Result(ChannelManagementJson.StableId("availability:" + state.ProviderItemId),
                "open", "AvailabilityIntentChanged", false);

        await using var lease = await availabilityState.TryLease(binding, cancellationToken);
        if (lease is null)
            return ChannelManagementJson.Result(ChannelManagementJson.StableId("availability:" + state.ProviderItemId),
                "reconciling", "AvailabilitySyncInProgress", false);
        return await ReadAndRecord(state, actorId, store, binding, source, paused, lease, cancellationToken);
    }

    private async Task<System.Text.Json.JsonElement> ReadAndRecord(ChannelAvailabilityState availabilityState, Guid actorId,
        TenantStoreBinding store, AvailabilityBinding binding, TenantAvailabilitySnapshot source, bool paused,
        IChannelAvailabilityLease lease, CancellationToken cancellationToken)
    {
        var operationId = ChannelManagementJson.StableId("availability:" + availabilityState.ProviderItemId);
        await audit.Record(binding, actorId, "AvailabilityReconcile", "Intent", operationId,
            context.Clock.GetUtcNow(), cancellationToken);
        var actual = await provider.Read(store, context.Webhook.ClientId, cancellationToken);
        if (!actual.Items.TryGetValue(availabilityState.ProviderItemId, out var observed))
            return ChannelManagementJson.Result(operationId, "open", "ProviderAvailabilityUnavailable", false);
        var sourceItem = source.Items.SingleOrDefault(row => row.ProviderItemId == availabilityState.ProviderItemId);
        if (sourceItem is null)
            return ChannelManagementJson.Result(operationId, "open", "AvailabilitySourceUnavailable", false);
        var matches = observed == (!paused && sourceItem.Available);
        var observedState = matches ? Verified : "Mismatch";
        await lease.Observe(availabilityState.ProviderItemId, availabilityState.SourceRevision, observedState, observed, actual.Hash,
            context.Clock.GetUtcNow(), cancellationToken);
        await audit.Record(binding, actorId, "AvailabilityReconcile", observedState, operationId,
            context.Clock.GetUtcNow(), cancellationToken);
        return ChannelManagementJson.Result(operationId, matches ? "resolved" : "open",
            matches ? null : "ProviderAvailabilityMismatch", false, context.Clock.GetUtcNow());
    }

    private static bool IsPaused(ChannelAvailabilityOverride? intent, DateTimeOffset now)
        => intent is { IsPaused: true } && (intent.PausedUntil is null || intent.PausedUntil > now);

    private static string CurrentRevision(string sourceRevision, ChannelAvailabilityOverride? intent, bool paused)
        => intent is null ? sourceRevision : ProviderJson.Hash(ProviderJson.Encode(new
        { sourceRevision, paused, updatedAt = intent.UpdatedAt, pausedUntil = intent.PausedUntil }));
}
