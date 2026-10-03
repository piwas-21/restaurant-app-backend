using System.Text.Json;
using RestaurantSystem.Channels.Domain;

namespace RestaurantSystem.Channels.Api;

public sealed partial class TenantChannelCatalogueService
{
    private async Task<JsonElement> CategoryCatalogueView(CatalogueMappingDraft? draft, CancellationToken cancellationToken)
    {
        if (draft is null || !TenantCatalogueCategorySnapshot.IsCategorySnapshot(draft.Snapshot))
            return await EmptyCategoryCatalogueView(draft, cancellationToken);
        var category = TenantCatalogueCategorySnapshot.Read(draft.Snapshot, draft.MappingRevision);
        var store = FromCategorySnapshot(draft.Snapshot, draft.MappingRevision);
        var latest = await state.Latest(context.Binding(store), cancellationToken);
        var source = await tenantCatalogue.Read(store, cancellationToken);
        var preview = await publication.Preview(menu.Preview(), store, cancellationToken);
        var providerMenu = await ProviderMenu(latest, cancellationToken);
        var planned = PlannedMenu(preview, menu.Preview());
        return ProviderJson.Encode(new
        {
            storeId = store.StoreId,
            currency = store.Currency,
            mappingRevision = draft.MappingRevision,
            draftRevision = draft.Revision,
            sourceRevision = category.SourceRevision,
            canPublish = ProviderJson.Flag(preview, CanPublishProperty),
            items = Rows(store, source, preview, providerMenu.Menu, menu.Preview(), providerMenu.Status),
            serviceAvailability = ServiceHours(planned),
            serviceHoursEditable = false,
            serviceHoursStatus = "reviewedTemplate",
            currentServiceAvailability = ServiceHours(providerMenu.Menu),
            currentServiceHoursStatus = HasServiceAvailability(providerMenu.Menu) ? providerMenu.Status : "unknown",
            blockingCodes = Blocks(preview),
            warningCodes = Array.Empty<string>(),
            latestPublication = PublicationSummary(latest),
            selectionMode = CategorySelectionMode,
            categoryBasis = category.CategoryBasis,
            categories = category.Categories.Select(CategoryDto),
            selectedItems = category.Items.Select(CategoryItemDto),
            taxProfile = TaxProfile(preview),
            taxProfileRevision = ProviderJson.Text(preview, "taxProfileRevision")
        });
    }

    private async Task<JsonElement> EmptyCategoryCatalogueView(CatalogueMappingDraft? draft,
        CancellationToken cancellationToken)
    {
        var source = await tenantCatalogue.Categories(context.ConfiguredStore, string.Empty, [], [], [], cancellationToken);
        var latest = await state.Latest(context.Binding(), cancellationToken);
        var providerMenu = await ProviderMenu(latest, cancellationToken);
        var plannedMenu = menu.Preview();
        return ProviderJson.Encode(new
        {
            storeId = context.ConfiguredStore.StoreId,
            currency = context.ConfiguredStore.Currency,
            mappingRevision = draft?.MappingRevision ?? context.ConfiguredStore.CatalogueRevision,
            draftRevision = draft?.Revision ?? string.Empty,
            sourceRevision = source.Revision,
            canPublish = false,
            items = Array.Empty<object>(),
            serviceAvailability = ServiceHours(plannedMenu),
            serviceHoursEditable = false,
            serviceHoursStatus = "reviewedTemplate",
            currentServiceAvailability = ServiceHours(providerMenu.Menu),
            currentServiceHoursStatus = HasServiceAvailability(providerMenu.Menu) ? providerMenu.Status : "unknown",
            blockingCodes = new[] { "SaveDraftBeforePublishing" },
            warningCodes = new[] { "SaveDraftBeforePublishing" },
            latestPublication = PublicationSummary(latest),
            selectionMode = "categoryItemsV1",
            categoryBasis = source.CategoryBasis,
            categories = source.Categories.Select(row => CategoryDto(row, null)),
            selectedItems = Array.Empty<object>(),
            taxProfile = (object?)null,
            taxProfileRevision = string.Empty
        });
    }

    private async Task<JsonElement> PreviewCategoryDraft(CatalogueMappingDraft draft, Guid actorId,
        CancellationToken cancellationToken)
    {
        var snapshot = TenantCatalogueCategorySnapshot.Read(draft.Snapshot, draft.MappingRevision);
        var store = FromCategorySnapshot(draft.Snapshot, draft.MappingRevision);
        _ = await tenantCatalogue.Read(store, cancellationToken);
        var preview = await publication.Preview(menu.Preview(), store, cancellationToken);
        var latest = await state.Latest(context.Binding(store), cancellationToken);
        var providerMenu = await ProviderMenu(latest, cancellationToken);
        var planned = PlannedMenu(preview, menu.Preview());
        await audit.Record(context.Binding(store), actorId, "CataloguePreview",
            ProviderJson.Flag(preview, CanPublishProperty) ? "Ready" : "Blocked", null,
            context.Clock.GetUtcNow(), cancellationToken);
        return ProviderJson.Encode(new
        {
            draftRevision = draft.Revision,
            mappingRevision = draft.MappingRevision,
            sourceRevision = snapshot.SourceRevision,
            publicationRevision = ProviderJson.Text(preview, "revision"),
            currency = store.Currency,
            canPublish = ProviderJson.Flag(preview, CanPublishProperty),
            items = Array.Empty<object>(),
            serviceAvailability = ServiceHours(planned),
            serviceHoursEditable = false,
            serviceHoursStatus = "reviewedTemplate",
            currentServiceAvailability = ServiceHours(providerMenu.Menu),
            currentServiceHoursStatus = HasServiceAvailability(providerMenu.Menu) ? providerMenu.Status : "unknown",
            blockingCodes = Blocks(preview),
            warningCodes = Array.Empty<string>(),
            selectionMode = CategorySelectionMode,
            categoryBasis = snapshot.CategoryBasis,
            selectedItems = ReadSelectedPreview(preview),
            taxProfile = TaxProfile(preview),
            taxProfileRevision = ProviderJson.Text(preview, "taxProfileRevision")
        });
    }

    private async Task<JsonElement> PublishCategoryDraft(TenantManagementPublishRequest request,
        CatalogueMappingDraft draft, Guid actorId, CancellationToken cancellationToken)
    {
        var store = FromCategorySnapshot(draft.Snapshot, draft.MappingRevision);
        var preview = await publication.Preview(menu.Preview(), store, cancellationToken);
        if (!ProviderJson.Flag(preview, CanPublishProperty))
            throw new ChannelConsoleException(409, "Resolve the listed catalogue blockers and refresh the preview.", "SelectionBlocked");
        RequireTaxConfirmation(request, preview);
        if (ProviderJson.Text(preview, "revision") != request.PublicationRevision)
            throw new ChannelConsoleException(409, "The source or reviewed menu changed. Refresh the preview before publishing.", "SourceRevisionChanged");

        await audit.Record(context.Binding(store), actorId, "CataloguePublish", "Intent", null,
            context.Clock.GetUtcNow(), cancellationToken);
        try
        {
            await publication.Publish(menu.Preview(), request.PublicationRevision, store, cancellationToken,
                async token => (await state.ReadDraft(context.Binding(), token))?.Revision == request.DraftRevision);
            var latest = await state.Latest(context.Binding(store), cancellationToken);
            var verified = latest is { State: CataloguePublicationStates.Verified }
                && latest.Revision == request.PublicationRevision;
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

    private static void RequireTaxConfirmation(TenantManagementPublishRequest request, JsonElement preview)
    {
        var taxRevision = ProviderJson.Text(preview, "taxProfileRevision");
        if (taxRevision.Length != TenantCatalogueLimits.RevisionLength)
            throw new ChannelConsoleException(409, "The reviewed sandbox tax profile is unavailable.", "ReviewedTaxProfileUnavailable");
        if (!request.ConfirmedTaxProfile)
            throw new ChannelConsoleException(409, "Confirm the reviewed sandbox tax profile before publishing.", "TaxProfileConfirmationRequired");
        if (request.TaxProfileRevision != taxRevision)
            throw new ChannelConsoleException(409, "The reviewed tax profile changed. Refresh the preview and confirm it again.", "TaxProfileChanged");
    }

    private TenantStoreBinding FromCategorySnapshot(JsonElement snapshot, string mappingRevision)
    {
        var saved = TenantCatalogueCategorySnapshot.Read(snapshot, mappingRevision);
        return context.Clone(context.ConfiguredStore, mappingRevision, saved.Items,
            new TenantCatalogueBindingSelection(saved.SourceRevision, saved.Language,
                saved.SelectedCategoryIds, saved.ItemOverrides, saved.Categories));
    }

    private static object? ReadSelectedPreview(JsonElement preview)
        => preview.TryGetProperty("selectedItems", out var rows) && rows.ValueKind == JsonValueKind.Array
            ? rows.Clone() : Array.Empty<object>();

    private static JsonElement? TaxProfile(JsonElement preview)
        => preview.ValueKind == JsonValueKind.Object && preview.TryGetProperty("taxProfile", out var profile)
            && profile.ValueKind == JsonValueKind.Object ? profile.Clone() : null;
}
