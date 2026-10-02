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
    private const string ReconcileAction = "AvailabilityReconcile";
    private const string IntentResult = "Intent";
    private const string IntentChangedResult = "IntentChanged";
    private const string IntentChangedCode = "AvailabilityIntentChanged";

    public async Task<System.Text.Json.JsonElement> Reconcile(ChannelAvailabilityState state, Guid actorId,
        CancellationToken cancellationToken)
    {
        // The lease API needs a revision-bound binding; this lookup only selects its lock key.
        var leaseStore = await availabilityState.Active(cancellationToken);
        var leaseBinding = context.Binding(leaseStore);
        var operationId = OperationId(state.ProviderItemId);
        await using var lease = await availabilityState.TryLease(leaseBinding, cancellationToken);
        if (lease is null)
            return ChannelManagementJson.Result(operationId, "reconciling", "AvailabilitySyncInProgress", false);

        var store = await availabilityState.Active(cancellationToken);
        var binding = context.Binding(store);
        if (!SameBinding(leaseBinding, binding)) return IntentChanged(operationId);

        var source = await tenantAvailability.Read(store, cancellationToken);
        var intent = await availabilityState.ReadOverride(binding, cancellationToken);
        var paused = IsPaused(intent, context.Clock.GetUtcNow());
        if (!IsCurrentRevision(source, intent, paused, state.SourceRevision)) return IntentChanged(operationId);

        return await ReadAndRecord(state, actorId, new(store, binding, source, paused), lease, cancellationToken);
    }

    private async Task<System.Text.Json.JsonElement> ReadAndRecord(ChannelAvailabilityState reconcileState, Guid actorId,
        AvailabilityReadContext snapshot, IChannelAvailabilityLease lease, CancellationToken cancellationToken)
    {
        var operationId = OperationId(reconcileState.ProviderItemId);
        await audit.Record(snapshot.Binding, actorId, ReconcileAction, IntentResult, operationId,
            context.Clock.GetUtcNow(), cancellationToken);
        var actual = await provider.Read(snapshot.Store, context.Webhook.ClientId, cancellationToken);
        if (!await IsCurrent(snapshot.Binding, reconcileState.SourceRevision, cancellationToken))
        {
            await audit.Record(snapshot.Binding, actorId, ReconcileAction, IntentChangedResult, operationId,
                context.Clock.GetUtcNow(), cancellationToken);
            return IntentChanged(operationId);
        }

        if (!actual.Items.TryGetValue(reconcileState.ProviderItemId, out var observed))
            return ChannelManagementJson.Result(operationId, "open", "ProviderAvailabilityUnavailable", false);
        var sourceItem = snapshot.Source.Items.SingleOrDefault(row => row.ProviderItemId == reconcileState.ProviderItemId);
        if (sourceItem is null)
            return ChannelManagementJson.Result(operationId, "open", "AvailabilitySourceUnavailable", false);
        var matches = observed == (!snapshot.Paused && sourceItem.Available);
        var observedState = matches ? Verified : "Mismatch";
        var recorded = await lease.Observe(reconcileState.ProviderItemId, reconcileState.SourceRevision,
            observedState, observed, actual.Hash, context.Clock.GetUtcNow(), cancellationToken);
        if (!recorded)
        {
            await audit.Record(snapshot.Binding, actorId, ReconcileAction, IntentChangedResult, operationId,
                context.Clock.GetUtcNow(), cancellationToken);
            return IntentChanged(operationId);
        }

        await audit.Record(snapshot.Binding, actorId, ReconcileAction, observedState, operationId,
            context.Clock.GetUtcNow(), cancellationToken);
        return ChannelManagementJson.Result(operationId, matches ? "resolved" : "open",
            matches ? null : "ProviderAvailabilityMismatch", false, context.Clock.GetUtcNow());
    }

    private async Task<bool> IsCurrent(AvailabilityBinding expectedBinding, string expectedRevision,
        CancellationToken cancellationToken)
    {
        var store = await availabilityState.Active(cancellationToken);
        var binding = context.Binding(store);
        if (!SameBinding(expectedBinding, binding)) return false;
        var source = await tenantAvailability.Read(store, cancellationToken);
        var intent = await availabilityState.ReadOverride(binding, cancellationToken);
        var paused = IsPaused(intent, context.Clock.GetUtcNow());
        return IsCurrentRevision(source, intent, paused, expectedRevision);
    }

    private static bool SameBinding(AvailabilityBinding left, AvailabilityBinding right)
        => left.ClientId == right.ClientId && left.StoreId == right.StoreId
            && left.TenantId == right.TenantId && left.CatalogueRevision == right.CatalogueRevision;

    private static bool IsCurrentRevision(TenantAvailabilitySnapshot source, ChannelAvailabilityOverride? intent,
        bool paused, string expectedRevision)
        => CurrentRevision(source.Revision, intent, paused) == expectedRevision;

    private static Guid OperationId(string providerItemId)
        => ChannelManagementJson.StableId("availability:" + providerItemId);

    private static System.Text.Json.JsonElement IntentChanged(Guid operationId)
        => ChannelManagementJson.Result(operationId, "open", IntentChangedCode, false);

    private sealed record AvailabilityReadContext(TenantStoreBinding Store, AvailabilityBinding Binding,
        TenantAvailabilitySnapshot Source, bool Paused);

    private static bool IsPaused(ChannelAvailabilityOverride? intent, DateTimeOffset now)
        => intent is { IsPaused: true } && (intent.PausedUntil is null || intent.PausedUntil > now);

    private static string CurrentRevision(string sourceRevision, ChannelAvailabilityOverride? intent, bool paused)
        => intent is null ? sourceRevision : ProviderJson.Hash(ProviderJson.Encode(new
        { sourceRevision, paused, updatedAt = intent.UpdatedAt, pausedUntil = intent.PausedUntil }));
}
