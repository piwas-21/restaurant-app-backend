using System.Text.Json;
using System.Net;
using RestaurantSystem.Channels.Domain;

namespace RestaurantSystem.Channels.Api;

public sealed partial class TenantChannelManagementOperations
{
    public async Task<JsonElement> Catalogue(CancellationToken cancellationToken)
    {
        var store = await DraftStore(cancellationToken);
        var draft = await _drafts.Read(Binding(), cancellationToken);
        var preview = await _publication.Preview(_menu.Preview(), store, cancellationToken);
        var latest = await _publications.Latest(Binding(store), cancellationToken);
        var source = await _tenantCatalogue.Read(store, cancellationToken);
        var providerMenu = await ProviderMenu(latest, cancellationToken);
        var plannedMenu = PlannedMenu(preview, _menu.Preview());
        return ProviderJson.Encode(new
        {
            storeId = store.StoreId,
            currency = store.Currency,
            mappingRevision = store.CatalogueRevision,
            draftRevision = draft?.Revision ?? string.Empty,
            sourceRevision = ProviderJson.Text(preview, "sourceRevision"),
            canPublish = ProviderJson.Flag(preview, "canPublish"),
            items = Rows(store, source, preview, providerMenu.Menu, _menu.Preview(), providerMenu.Status),
            serviceAvailability = ServiceHours(plannedMenu),
            serviceHoursEditable = false,
            serviceHoursStatus = "reviewedTemplate",
            currentServiceAvailability = ServiceHours(providerMenu.Menu),
            currentServiceHoursStatus = HasServiceAvailability(providerMenu.Menu) ? providerMenu.Status : "unknown",
            blockingCodes = Blocks(preview),
            warningCodes = draft is null ? new[] { "SaveDraftBeforePublishing" } : Array.Empty<string>(),
            latestPublication = PublicationSummary(latest)
        });
    }

    public async Task<JsonElement> SaveDraft(TenantManagementDraftRequest request, Guid actorId,
        CancellationToken cancellationToken)
    {
        RequireEnabled();
        await using var lease = await _availabilityJobs.TryLease(Binding(), cancellationToken);
        if (lease is null) throw new ChannelConsoleException(409, "A menu or availability operation is in progress. Reload before changing mappings.");
        var requested = ValidateMappings(request.Items);
        var sourceItems = requested.Select(row => new TenantItemMapping
        {
            ProviderItemId = row.ProviderItemId,
            ProductId = row.ProductId,
            VariationId = row.VariationId
        }).ToArray();
        var sourceStore = Clone(ConfiguredStore, ConfiguredStore.CatalogueRevision, sourceItems);
        var snapshot = await _tenantCatalogue.Read(sourceStore, cancellationToken);
        var mapped = requested.Select(mapping =>
        {
            var selected = snapshot.Items.SingleOrDefault(row => row.ProductId == mapping.ProductId && row.VariationId == mapping.VariationId)
                ?? throw new ChannelConsoleException(400, "Select a product or variation from this tenant's catalogue.");
            if (selected.BlockReason.Length > 0) throw new ChannelConsoleException(400, "Select only supported, available Sofra products.");
            return new TenantItemMapping
            {
                ProviderItemId = mapping.ProviderItemId,
                ProductId = mapping.ProductId,
                VariationId = mapping.VariationId,
                VariationName = selected.VariationName
            };
        }).ToArray();
        var mappingRevision = ManagementMappingRevision(mapped);
        var store = Clone(ConfiguredStore, mappingRevision, mapped);
        var pending = await _publications.Latest(Binding(store), cancellationToken);
        if (pending is { State: CataloguePublicationStates.Pending }
            && pending.MappingHash != CatalogueMenuPlanner.MappingHash(store))
            throw new ChannelConsoleException(409, "A previous menu publication is unresolved. Reconcile its provider readback before changing product mappings.");
        var revision = Guid.NewGuid().ToString("D"); var now = _clock.GetUtcNow();
        var draft = new CatalogueMappingDraft(revision, mappingRevision, MappingSnapshot(mappingRevision, mapped), actorId, now);
        await _audit.Record(Binding(store), actorId, "CatalogueDraftSave", "Intent", null, now, cancellationToken);
        if (!await _drafts.Save(Binding(), draft, request.ExpectedDraftRevision, cancellationToken))
            throw new ChannelConsoleException(409, "The catalogue draft changed in another session. Reload before saving.");
        await _audit.Record(Binding(store), actorId, "CatalogueDraftSaved", "Saved", null, _clock.GetUtcNow(), cancellationToken);
        var providerMenu = await ProviderMenu(pending, cancellationToken);
        return ProviderJson.Encode(new
        {
            draftRevision = revision,
            mappingRevision,
            updatedAt = now,
            items = Rows(store, snapshot, default, providerMenu.Menu, _menu.Preview(), providerMenu.Status)
        });
    }

    public async Task<JsonElement> Preview(TenantManagementPreviewRequest request, Guid actorId, CancellationToken cancellationToken)
    {
        RequireEnabled();
        if (!Guid.TryParseExact(request.DraftRevision, "D", out _)) throw new ChannelConsoleException(400, "Reload the saved draft before previewing.");
        var draft = await _drafts.Read(Binding(), cancellationToken);
        if (draft is null || draft.Revision != request.DraftRevision) throw new ChannelConsoleException(409, "The catalogue draft changed. Reload and review it again.");
        var store = FromSnapshot(draft.Snapshot, draft.MappingRevision);
        var preview = await _publication.Preview(_menu.Preview(), store, cancellationToken);
        var source = await _tenantCatalogue.Read(store, cancellationToken);
        var latest = await _publications.Latest(Binding(store), cancellationToken);
        var providerMenu = await ProviderMenu(latest, cancellationToken);
        var plannedMenu = PlannedMenu(preview, _menu.Preview());
        var now = _clock.GetUtcNow();
        await _audit.Record(Binding(store), actorId, "CataloguePreview", ProviderJson.Flag(preview, "canPublish") ? "Ready" : "Blocked",
            null, now, cancellationToken);
        return ProviderJson.Encode(new
        {
            draftRevision = draft.Revision,
            mappingRevision = draft.MappingRevision,
            sourceRevision = ProviderJson.Text(preview, "sourceRevision"),
            publicationRevision = ProviderJson.Text(preview, "revision"),
            currency = store.Currency,
            canPublish = ProviderJson.Flag(preview, "canPublish"),
            items = Rows(store, source, preview, providerMenu.Menu, _menu.Preview(), providerMenu.Status),
            serviceAvailability = ServiceHours(plannedMenu),
            serviceHoursEditable = false,
            serviceHoursStatus = "reviewedTemplate",
            currentServiceAvailability = ServiceHours(providerMenu.Menu),
            currentServiceHoursStatus = HasServiceAvailability(providerMenu.Menu) ? providerMenu.Status : "unknown",
            blockingCodes = Blocks(preview),
            warningCodes = Array.Empty<string>()
        });
    }

    public async Task<JsonElement> Publish(TenantManagementPublishRequest request, Guid actorId,
        CancellationToken cancellationToken)
    {
        RequireEnabled();
        var draft = await _drafts.Read(Binding(), cancellationToken);
        if (draft is null || draft.Revision != request.DraftRevision)
            throw new ChannelConsoleException(409, "The saved catalogue draft changed after preview. Review a fresh preview before publishing.");
        var store = FromSnapshot(draft.Snapshot, draft.MappingRevision);
        var preview = await _publication.Preview(_menu.Preview(), store, cancellationToken);
        if (!ProviderJson.Flag(preview, "canPublish") || ProviderJson.Text(preview, "revision") != request.PublicationRevision)
            throw new ChannelConsoleException(409, "The source or reviewed menu changed. Refresh the preview before publishing.");
        var now = _clock.GetUtcNow();
        await _audit.Record(Binding(store), actorId, "CataloguePublish", "Intent", null, now, cancellationToken);
        try
        {
            await _publication.Publish(_menu.Preview(), request.PublicationRevision, store, cancellationToken,
                async token => (await _drafts.Read(Binding(), token))?.Revision == request.DraftRevision);
            var latest = await _publications.Latest(Binding(store), cancellationToken);
            var verified = latest is { State: CataloguePublicationStates.Verified } && latest.Revision == request.PublicationRevision;
            await _audit.Record(Binding(store), actorId, "CataloguePublish", verified ? "Verified" : "Unconfirmed",
                latest?.Id, _clock.GetUtcNow(), cancellationToken);
            return PublicationDto(latest, verified);
        }
        catch (ChannelConsoleException)
        {
            var latest = await _publications.Latest(Binding(store), cancellationToken);
            await _audit.Record(Binding(store), actorId, "CataloguePublish", "Unconfirmed", latest?.Id,
                _clock.GetUtcNow(), cancellationToken);
            if (latest is { State: CataloguePublicationStates.Pending } && latest.Revision == request.PublicationRevision)
                return PublicationDto(latest, false);
            throw;
        }
    }

    public async Task<JsonElement> Publication(Guid publicationId, CancellationToken cancellationToken)
    {
        RequireEnabled();
        var publication = await _publications.Find(Binding(), publicationId, cancellationToken);
        if (publication is null) throw new ChannelConsoleException(404, "Catalogue publication was not found.");
        return PublicationDto(publication, publication.State == CataloguePublicationStates.Verified);
    }

    private TenantManagementMapping[] ValidateMappings(IReadOnlyList<TenantManagementMapping> requested)
    {
        var configured = ConfiguredStore.Items.Select(row => row.ProviderItemId).ToHashSet(StringComparer.Ordinal);
        if (requested is null || requested.Count != configured.Count || requested.Any(row => row.ProductId == Guid.Empty
            || !configured.Contains(row.ProviderItemId)) || requested.Select(row => row.ProviderItemId).Distinct(StringComparer.Ordinal).Count() != requested.Count
            || requested.Select(row => (row.ProductId, row.VariationId)).Distinct().Count() != requested.Count)
            throw new ChannelConsoleException(400, "Map each approved Uber menu item to a different supported Sofra product.");
        return requested.OrderBy(row => row.ProviderItemId, StringComparer.Ordinal).ToArray();
    }

}
