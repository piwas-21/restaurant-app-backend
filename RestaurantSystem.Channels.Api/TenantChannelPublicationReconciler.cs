using RestaurantSystem.Channels.Domain;

namespace RestaurantSystem.Channels.Api;

public interface ITenantChannelPublicationReconciler
{
    Task<System.Text.Json.JsonElement> Reconcile(CataloguePublication publication, Guid actorId,
        CancellationToken cancellationToken);
}

public sealed class TenantChannelPublicationReconciler(TenantManagementContext context,
    ITenantCatalogueManagementState state, ISandboxMenu menu, IChannelManagementAudit audit)
    : ITenantChannelPublicationReconciler
{
    private const string Action = "PublicationReconcile";
    private const string Resolved = "resolved";
    private const string Uncertain = "uncertain";

    public async Task<System.Text.Json.JsonElement> Reconcile(CataloguePublication publication, Guid actorId,
        CancellationToken cancellationToken)
    {
        if (publication.State == CataloguePublicationStates.Verified)
            return ChannelManagementJson.Result(publication.Id, Resolved, null, false);
        if (publication.State != CataloguePublicationStates.Pending)
            return ChannelManagementJson.Result(publication.Id, "open", "PublicationNoLongerPending", false);
        if (!TryBinding(publication, out var binding))
            return ChannelManagementJson.Result(publication.Id, Uncertain, "MappingSnapshotUnavailable", false);

        await using var lease = await state.TryLease(binding, cancellationToken);
        if (lease is null)
            return ChannelManagementJson.Result(publication.Id, "reconciling", "PublicationInProgress", false);
        var current = await state.Find(binding, publication.Id, cancellationToken);
        if (current is null) throw new ChannelConsoleException(404, "Publication was not found for this store.");
        var latest = await state.Latest(binding, cancellationToken);
        if (latest?.Id != current.Id)
            return ChannelManagementJson.Result(publication.Id, Uncertain, "A newer publication requires review", false);
        if (latest.State == CataloguePublicationStates.Verified)
            return ChannelManagementJson.Result(latest.Id, Resolved, null, false);
        if (latest.State != CataloguePublicationStates.Pending)
            return ChannelManagementJson.Result(latest.Id, "open", "PublicationNoLongerPending", false);
        return await ReconcileCurrent(latest, binding, actorId, cancellationToken);
    }

    private async Task<System.Text.Json.JsonElement> ReconcileCurrent(CataloguePublication current,
        AvailabilityBinding binding, Guid actorId, CancellationToken cancellationToken)
    {
        var intentAt = context.Clock.GetUtcNow();
        await audit.Record(binding, actorId, Action, "Intent", current.Id, intentAt, cancellationToken);
        var actual = await menu.Read(cancellationToken);
        var checkedAt = context.Clock.GetUtcNow();
        if (Matches(current.Menu, actual))
            return await Verify(current, binding, actorId, actual, checkedAt, cancellationToken);
        if (Matches(current.PreviousMenu, actual))
            return await Abandon(current, binding, actorId, checkedAt, cancellationToken);
        await audit.Record(binding, actorId, Action, "Unconfirmed", current.Id, checkedAt, cancellationToken);
        return ChannelManagementJson.Result(current.Id, Uncertain, "ProviderMenuDiffersFromExpectedAndPrevious", false, checkedAt);
    }

    private async Task<System.Text.Json.JsonElement> Verify(CataloguePublication publication, AvailabilityBinding binding,
        Guid actorId, System.Text.Json.JsonElement actual, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var verified = await state.Verify(binding, publication.Id, ProviderJson.Hash(actual), now, cancellationToken);
        await audit.Record(binding, actorId, Action, verified ? "Verified" : "Unconfirmed", publication.Id, now, cancellationToken);
        return ChannelManagementJson.Result(publication.Id, verified ? Resolved : Uncertain,
            verified ? null : "PublicationUnconfirmed", false, now);
    }

    private async Task<System.Text.Json.JsonElement> Abandon(CataloguePublication publication, AvailabilityBinding binding,
        Guid actorId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var abandoned = await state.Abandon(binding, publication.Id, cancellationToken);
        await audit.Record(binding, actorId, Action, abandoned ? "Abandoned" : "Unconfirmed", publication.Id, now, cancellationToken);
        return ChannelManagementJson.Result(publication.Id, abandoned ? Resolved : Uncertain,
            abandoned ? "PreviousMenuConfirmed" : "PublicationUnconfirmed", false, now);
    }

    private bool TryBinding(CataloguePublication publication, out AvailabilityBinding binding)
    {
        var revision = publication.MappingSnapshot is { } snapshot
            ? ProviderJson.Text(snapshot, "catalogueRevision") : string.Empty;
        binding = new(context.Webhook.ClientId, context.ConfiguredStore.StoreId, context.ConfiguredStore.TenantId, revision);
        return revision.Length > 0;
    }

    private static bool Matches(System.Text.Json.JsonElement expected, System.Text.Json.JsonElement actual)
    {
        try { SandboxMenuVerifier.Require(CatalogueMenuPlanner.Structural(expected), actual); return true; }
        catch (ChannelConsoleException) { return false; }
    }
}
