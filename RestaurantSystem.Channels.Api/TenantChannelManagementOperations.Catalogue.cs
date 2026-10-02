using System.Text.Json;
using System.Net;
using RestaurantSystem.Channels.Domain;

namespace RestaurantSystem.Channels.Api;

public sealed partial class TenantChannelCatalogueService(TenantManagementContext context,
    ITenantCatalogueManagementState state, ISandboxMenu menu, ITenantCatalogueClient tenantCatalogue,
    ITenantCataloguePublication publication, IChannelManagementAudit audit) : ITenantChannelCatalogueService
{
    private const string CanPublishProperty = "canPublish";

    public async Task<JsonElement> Catalogue(CancellationToken cancellationToken)
    {
        var store = await DraftStore(cancellationToken);
        var draft = await state.ReadDraft(context.Binding(), cancellationToken);
        var preview = await publication.Preview(menu.Preview(), store, cancellationToken);
        var latest = await state.Latest(context.Binding(store), cancellationToken);
        var source = await tenantCatalogue.Read(store, cancellationToken);
        var providerMenu = await ProviderMenu(latest, cancellationToken);
        var plannedMenu = PlannedMenu(preview, menu.Preview());
        return ProviderJson.Encode(new
        {
            storeId = store.StoreId,
            currency = store.Currency,
            mappingRevision = store.CatalogueRevision,
            draftRevision = draft?.Revision ?? string.Empty,
            sourceRevision = ProviderJson.Text(preview, "sourceRevision"),
            canPublish = ProviderJson.Flag(preview, CanPublishProperty),
            items = Rows(store, source, preview, providerMenu.Menu, menu.Preview(), providerMenu.Status),
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
        context.RequireEnabled();
        await using var lease = await state.TryLease(context.Binding(), cancellationToken);
        if (lease is null) throw new ChannelConsoleException(409, "A menu or availability operation is in progress. Reload before changing mappings.");
        var requested = ValidateMappings(request.Items);
        var sourceItems = requested.Select(row => new TenantItemMapping
        {
            ProviderItemId = row.ProviderItemId,
            ProductId = row.ProductId,
            VariationId = row.VariationId
        }).ToArray();
        var sourceStore = context.Clone(context.ConfiguredStore, context.ConfiguredStore.CatalogueRevision, sourceItems);
        var snapshot = await tenantCatalogue.Read(sourceStore, cancellationToken);
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
        var store = context.Clone(context.ConfiguredStore, mappingRevision, mapped);
        var pending = await state.Latest(context.Binding(store), cancellationToken);
        if (pending is { State: CataloguePublicationStates.Pending }
            && pending.MappingHash != CatalogueMenuPlanner.MappingHash(store))
            throw new ChannelConsoleException(409, "A previous menu publication is unresolved. Reconcile its provider readback before changing product mappings.");
        var revision = Guid.NewGuid().ToString("D"); var now = context.Clock.GetUtcNow();
        var draft = new CatalogueMappingDraft(revision, mappingRevision, MappingSnapshot(mappingRevision, mapped), actorId, now);
        await audit.Record(context.Binding(store), actorId, "CatalogueDraftSave", "Intent", null, now, cancellationToken);
        if (!await state.SaveDraft(context.Binding(), draft, request.ExpectedDraftRevision, cancellationToken))
            throw new ChannelConsoleException(409, "The catalogue draft changed in another session. Reload before saving.");
        await audit.Record(context.Binding(store), actorId, "CatalogueDraftSaved", "Saved", null, context.Clock.GetUtcNow(), cancellationToken);
        var providerMenu = await ProviderMenu(pending, cancellationToken);
        return ProviderJson.Encode(new
        {
            draftRevision = revision,
            mappingRevision,
            updatedAt = now,
            items = Rows(store, snapshot, default, providerMenu.Menu, menu.Preview(), providerMenu.Status)
        });
    }

    public async Task<JsonElement> Preview(TenantManagementPreviewRequest request, Guid actorId, CancellationToken cancellationToken)
    {
        context.RequireEnabled();
        if (!Guid.TryParseExact(request.DraftRevision, "D", out _)) throw new ChannelConsoleException(400, "Reload the saved draft before previewing.");
        var draft = await state.ReadDraft(context.Binding(), cancellationToken);
        if (draft is null || draft.Revision != request.DraftRevision) throw new ChannelConsoleException(409, "The catalogue draft changed. Reload and review it again.");
        var store = FromSnapshot(draft.Snapshot, draft.MappingRevision);
        var preview = await publication.Preview(menu.Preview(), store, cancellationToken);
        var source = await tenantCatalogue.Read(store, cancellationToken);
        var latest = await state.Latest(context.Binding(store), cancellationToken);
        var providerMenu = await ProviderMenu(latest, cancellationToken);
        var plannedMenu = PlannedMenu(preview, menu.Preview());
        var now = context.Clock.GetUtcNow();
        await audit.Record(context.Binding(store), actorId, "CataloguePreview", ProviderJson.Flag(preview, CanPublishProperty) ? "Ready" : "Blocked",
            null, now, cancellationToken);
        return ProviderJson.Encode(new
        {
            draftRevision = draft.Revision,
            mappingRevision = draft.MappingRevision,
            sourceRevision = ProviderJson.Text(preview, "sourceRevision"),
            publicationRevision = ProviderJson.Text(preview, "revision"),
            currency = store.Currency,
            canPublish = ProviderJson.Flag(preview, CanPublishProperty),
            items = Rows(store, source, preview, providerMenu.Menu, menu.Preview(), providerMenu.Status),
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
        context.RequireEnabled();
        var draft = await state.ReadDraft(context.Binding(), cancellationToken);
        if (draft is null || draft.Revision != request.DraftRevision)
            throw new ChannelConsoleException(409, "The saved catalogue draft changed after preview. Review a fresh preview before publishing.");
        var store = FromSnapshot(draft.Snapshot, draft.MappingRevision);
        var preview = await publication.Preview(menu.Preview(), store, cancellationToken);
        if (!ProviderJson.Flag(preview, CanPublishProperty) || ProviderJson.Text(preview, "revision") != request.PublicationRevision)
            throw new ChannelConsoleException(409, "The source or reviewed menu changed. Refresh the preview before publishing.");
        var now = context.Clock.GetUtcNow();
        await audit.Record(context.Binding(store), actorId, "CataloguePublish", "Intent", null, now, cancellationToken);
        try
        {
            await publication.Publish(menu.Preview(), request.PublicationRevision, store, cancellationToken,
                async token => (await state.ReadDraft(context.Binding(), token))?.Revision == request.DraftRevision);
            var latest = await state.Latest(context.Binding(store), cancellationToken);
            var verified = latest is { State: CataloguePublicationStates.Verified } && latest.Revision == request.PublicationRevision;
            await audit.Record(context.Binding(store), actorId, "CataloguePublish", verified ? "Verified" : "Unconfirmed",
                latest?.Id, context.Clock.GetUtcNow(), cancellationToken);
            return PublicationDto(latest, verified);
        }
        catch (ChannelConsoleException)
        {
            var latest = await state.Latest(context.Binding(store), cancellationToken);
            await audit.Record(context.Binding(store), actorId, "CataloguePublish", "Unconfirmed", latest?.Id,
                context.Clock.GetUtcNow(), cancellationToken);
            if (latest is { State: CataloguePublicationStates.Pending } && latest.Revision == request.PublicationRevision)
                return PublicationDto(latest, false);
            throw;
        }
    }

    public async Task<JsonElement> Publication(Guid publicationId, CancellationToken cancellationToken)
    {
        context.RequireEnabled();
        var found = await state.Find(context.Binding(), publicationId, cancellationToken);
        if (found is null) throw new ChannelConsoleException(404, "Catalogue publication was not found.");
        return PublicationDto(found, found.State == CataloguePublicationStates.Verified);
    }

    private TenantManagementMapping[] ValidateMappings(IReadOnlyList<TenantManagementMapping> requested)
    {
        var configured = context.ConfiguredStore.Items.Select(row => row.ProviderItemId).ToHashSet(StringComparer.Ordinal);
        if (requested is null || requested.Count != configured.Count || requested.Any(row => row.ProductId == Guid.Empty
            || !configured.Contains(row.ProviderItemId)) || requested.Select(row => row.ProviderItemId).Distinct(StringComparer.Ordinal).Count() != requested.Count
            || requested.Select(row => (row.ProductId, row.VariationId)).Distinct().Count() != requested.Count)
            throw new ChannelConsoleException(400, "Map each approved Uber menu item to a different supported Sofra product.");
        return requested.OrderBy(row => row.ProviderItemId, StringComparer.Ordinal).ToArray();
    }

    private async Task<TenantStoreBinding> DraftStore(CancellationToken cancellationToken)
    {
        context.RequireEnabled();
        var draft = await state.ReadDraft(context.Binding(), cancellationToken);
        if (draft is not null) return FromSnapshot(draft.Snapshot, draft.MappingRevision);
        try { return await state.Active(cancellationToken); }
        catch (ChannelConsoleException) { return context.ConfiguredStore; }
    }

    private TenantStoreBinding FromSnapshot(JsonElement snapshot, string mappingRevision)
    {
        if (snapshot.ValueKind != JsonValueKind.Object || !snapshot.TryGetProperty("catalogueRevision", out var revision)
            || revision.GetString() != mappingRevision || !snapshot.TryGetProperty("items", out var rows)
            || rows.ValueKind != JsonValueKind.Array) throw new ChannelConsoleException(409, "Saved catalogue mapping needs operator review.");
        var items = JsonSerializer.Deserialize<List<TenantItemMapping>>(rows.GetRawText(), new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var configuredItems = context.ConfiguredStore.Items;
        if (items is null || items.Count != configuredItems.Count
            || !items.Select(row => row.ProviderItemId).ToHashSet(StringComparer.Ordinal)
                .SetEquals(configuredItems.Select(row => row.ProviderItemId))
            || items.Select(row => row.ProviderItemId).Distinct(StringComparer.Ordinal).Count() != items.Count
            || items.Select(row => (row.ProductId, row.VariationId)).Distinct().Count() != items.Count
            || items.Any(row => row.ProductId == Guid.Empty || row.VariationId == Guid.Empty)
            || ManagementMappingRevision(items) != mappingRevision)
            throw new ChannelConsoleException(409, "Saved catalogue mapping needs operator review.");
        return context.Clone(context.ConfiguredStore, mappingRevision, items);
    }

    private string ManagementMappingRevision(IEnumerable<TenantItemMapping> items)
        => ProviderJson.Hash(ProviderJson.Encode(new
        {
            tenantId = context.ConfiguredStore.TenantId,
            storeId = context.ConfiguredStore.StoreId,
            currency = context.ConfiguredStore.Currency,
            items = items.OrderBy(row => row.ProviderItemId, StringComparer.Ordinal).Select(row => new
            { row.ProviderItemId, row.ProductId, row.VariationId })
        }));

    private static JsonElement MappingSnapshot(string revision, IEnumerable<TenantItemMapping> items)
        => ProviderJson.Encode(new
        {
            catalogueRevision = revision,
            items = items.OrderBy(row => row.ProviderItemId, StringComparer.Ordinal)
                .Select(row => new
                {
                    providerItemId = row.ProviderItemId,
                    productId = row.ProductId,
                    variationId = row.VariationId,
                    variationName = row.VariationName
                })
        });

}
