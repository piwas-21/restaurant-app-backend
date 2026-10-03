using System.Text.Json;
using RestaurantSystem.Channels.Domain;

namespace RestaurantSystem.Channels.Api;

public sealed partial class TenantChannelCatalogueService
{
    private const string CategorySelectionMode = "categoryItemsV1";
    private async Task<JsonElement> SaveCategoryDraft(TenantManagementDraftRequest request, Guid actorId,
        CancellationToken cancellationToken)
    {
        RequireCategoryMode();
        ValidateCategoryDraftRequest(request);
        await using var lease = await state.TryLease(context.Binding(), cancellationToken);
        if (lease is null) throw new ChannelConsoleException(409, "A menu or availability operation is in progress. Reload before changing mappings.");
        var current = await state.ReadDraft(context.Binding(), cancellationToken);
        if (current?.Revision != request.ExpectedDraftRevision)
            throw new ChannelConsoleException(409, "The catalogue draft changed in another session. Reload before saving.", "DraftRevisionConflict");

        var requestedOverrides = request.ItemOverrides.Select(row => new TenantCatalogueItemOverride(
            row.ProductId, row.VariationId, row.CategoryId, row.Selected)).ToArray();
        var selection = await tenantCatalogue.ReadSelection(context.ConfiguredStore, request.ExpectedSourceRevision,
            request.CategoryIds, requestedOverrides, cancellationToken);
        if (selection.Items.Count is < 1 or > TenantCatalogueLimits.SelectionCount)
            throw new ChannelConsoleException(400, "Select between 1 and 200 marketplace items.", "SelectionRequired");
        var categories = SelectionCategories(selection.Categories);
        var items = SelectionItems(selection.Items);
        var overrides = SelectionOverrides(selection.ItemOverrides);
        var mappingRevision = CategoryMappingRevision(context.ConfiguredStore, selection, categories, items, overrides);
        var frozen = new TenantCatalogueBindingSelection(selection.Revision, selection.Language,
            selection.SelectedCategoryIds, overrides, categories);
        var store = context.Clone(context.ConfiguredStore, mappingRevision, items, frozen);
        var latest = await state.Latest(context.Binding(store), cancellationToken);
        if (latest is { State: CataloguePublicationStates.Pending }
            && latest.MappingHash != CatalogueMenuPlanner.MappingHash(store))
            throw new ChannelConsoleException(409, "A previous menu publication is unresolved. Reconcile its provider readback before changing product selections.");

        var revision = Guid.NewGuid().ToString("D");
        var now = context.Clock.GetUtcNow();
        var snapshot = TenantCatalogueCategorySnapshot.Create(mappingRevision, selection.Revision,
            selection.Language, selection.SelectedCategoryIds, overrides, categories, items);
        var draft = new CatalogueMappingDraft(revision, mappingRevision, snapshot, actorId, now);
        await audit.Record(context.Binding(store), actorId, "CatalogueDraftSave", "Intent", null, now, cancellationToken);
        if (!await state.SaveDraft(context.Binding(), draft, request.ExpectedDraftRevision, cancellationToken))
            throw new ChannelConsoleException(409, "The catalogue draft changed in another session. Reload before saving.", "DraftRevisionConflict");
        await audit.Record(context.Binding(store), actorId, "CatalogueDraftSaved", "Saved", null,
            context.Clock.GetUtcNow(), cancellationToken);
        return ProviderJson.Encode(new
        {
            draftRevision = revision,
            mappingRevision,
            updatedAt = now,
            items = Array.Empty<object>(),
            selectionMode = CategorySelectionMode,
            categoryBasis = "primaryCategory",
            sourceRevision = selection.Revision,
            language = selection.Language,
            selectedCategoryIds = selection.SelectedCategoryIds,
            itemOverrides = overrides.Select(CategoryOverrideDto),
            categories = categories.Select(CategoryDto),
            selectedItems = items.Select(CategoryItemDto)
        });
    }

    private static void ValidateCategoryDraftRequest(TenantManagementDraftRequest request)
    {
        if (request is null || request.Items is null || request.CategoryIds is null || request.ItemOverrides is null
            || request.ExpectedSourceRevision is null)
            throw new ChannelConsoleException(400, "Refresh the tenant catalogue and review the selected categories and products.");
        if (request.CategoryIds.Count > TenantCatalogueLimits.MaximumCategoryReferences)
            throw new ChannelConsoleException(400, "Review no more than 1,000 categories at a time.", "CategoryLimitExceeded");
        if (request.ItemOverrides.Count > TenantCatalogueLimits.MaximumItemOverrides)
            throw new ChannelConsoleException(400, "Review no more than 2,000 individual item overrides at a time.", "SelectionOverrideLimitExceeded");
        if (request.Items.Count != 0 || !Revision(request.ExpectedSourceRevision)
            || request.CategoryIds.Distinct().Count() != request.CategoryIds.Count
            || request.CategoryIds.Any(id => id == Guid.Empty)
            || request.ItemOverrides.Any(row => row is null || row.ProductId == Guid.Empty || row.VariationId == Guid.Empty
                || row.CategoryId is null || row.CategoryId == Guid.Empty)
            || request.ItemOverrides.Select(row => (row.ProductId, row.VariationId)).Distinct().Count() != request.ItemOverrides.Count
            || request.ExpectedDraftRevision is { Length: > 0 } expected && !Guid.TryParseExact(expected, "D", out _))
            throw new ChannelConsoleException(400, "Refresh the tenant catalogue and review the selected categories and products.");
    }

    private static TenantCategoryMapping[] SelectionCategories(IReadOnlyList<TenantCatalogueSelectionCategory> categories)
        => categories.OrderBy(row => row.DisplayOrder).ThenBy(row => row.CategoryId).Select(row => new TenantCategoryMapping
        {
            CategoryId = row.CategoryId,
            ProviderCategoryId = TenantCatalogueSelectionIds.Category(row.CategoryId),
            Name = row.Name,
            DisplayOrder = row.DisplayOrder,
            Active = row.Active,
            TotalItemCount = row.TotalItemCount,
            SupportedItemCount = row.SupportedItemCount,
            UnsupportedItemCount = row.UnsupportedItemCount,
            SelectedItemCount = row.SelectedItemCount,
            SelectedUnsupportedItemCount = row.SelectedUnsupportedItemCount
        }).ToArray();

    private static TenantItemMapping[] SelectionItems(IReadOnlyList<TenantCatalogueSelectionItem> items)
        => items.Select(row => new TenantItemMapping
        {
            ProviderItemId = TenantCatalogueSelectionIds.Item(row.ProductId, row.VariationId),
            ProductId = row.ProductId,
            VariationId = row.VariationId,
            VariationName = row.VariationName,
            SelectionKey = row.SelectionKey,
            CategoryId = row.CategoryId,
            CategoryName = row.CategoryName,
            CategoryDisplayOrder = row.CategoryDisplayOrder,
            ItemDisplayOrder = row.ItemDisplayOrder,
            SourceFingerprint = row.SourceFingerprint,
            ItemName = row.Name,
            Description = row.Description,
            PriceMinor = row.PriceMinor,
            Available = row.Available,
            Supported = row.Supported,
            BlockReason = row.BlockReason
        }).ToArray();

    private static TenantItemMappingOverride[] SelectionOverrides(IReadOnlyList<TenantCatalogueItemOverride> rows)
        => rows.Select(row => new TenantItemMappingOverride
        {
            ProductId = row.ProductId,
            VariationId = row.VariationId,
            CategoryId = row.CategoryId,
            Selected = row.Selected,
            SelectionKey = row.SelectionKey,
            SourceFingerprint = row.SourceFingerprint,
            Supported = row.Supported
        }).ToArray();

    private static string CategoryMappingRevision(TenantStoreBinding configured, TenantCatalogueSelection selection,
        IReadOnlyList<TenantCategoryMapping> categories, IReadOnlyList<TenantItemMapping> items,
        IReadOnlyList<TenantItemMappingOverride> overrides)
        => ProviderJson.Hash(ProviderJson.Encode(new
        {
            tenantId = configured.TenantId,
            storeId = configured.StoreId,
            currency = configured.Currency,
            selection.Revision,
            selection.Language,
            selectedCategoryIds = selection.SelectedCategoryIds.Order(),
            itemOverrides = overrides.OrderBy(row => row.CategoryId).ThenBy(row => row.ProductId).ThenBy(row => row.VariationId),
            categories,
            items
        }));

    private static bool Revision(string value)
        => value.Length == TenantCatalogueLimits.RevisionLength && value.All(char.IsAsciiHexDigitLower);

    private void RequireCategoryMode()
    {
        context.RequireCategorySelection();
        if (!context.Management.CategorySelectionEnabled)
            throw new ChannelConsoleException(404, "ModuleNotEnabled");
    }
}
