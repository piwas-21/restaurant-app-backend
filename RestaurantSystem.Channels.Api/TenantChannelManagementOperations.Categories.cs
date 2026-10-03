using System.Text.Json;

namespace RestaurantSystem.Channels.Api;

public sealed partial class TenantChannelCatalogueService
{
    private const int MaximumCategoryMenuItems = TenantCatalogueLimits.SelectionCount;

    public async Task<JsonElement> CheckCategorySelection(TenantManagementCategoryChangesRequest request,
        CancellationToken cancellationToken)
    {
        RequireCategoryMode();
        ValidateCategoryReferences(request);
        var overrides = request.ItemOverrides.Select(row => new TenantCatalogueItemOverride(
            row.ProductId, row.VariationId, row.CategoryId, row.Selected)).ToArray();
        var source = await tenantCatalogue.Categories(context.ConfiguredStore, request.ExpectedSourceRevision,
            request.CategoryIds, request.ItemReferences, overrides, cancellationToken);
        return ProviderJson.Encode(new
        {
            sourceRevision = source.Revision,
            language = source.Language,
            sourceChanged = source.SourceChanged,
            categoryBasis = source.CategoryBasis,
            maximumCategoryCount = source.MaximumCategoryCount,
            maximumItemOverrideCount = source.MaximumItemOverrideCount,
            categories = source.Categories.Select(row => CategoryDto(row, null)),
            removedCategoryIds = source.RemovedCategoryIds,
            removedItems = source.RemovedItems.Select(RemovedOverrideDto),
            removedItemOverrides = source.RemovedItemOverrides.Select(RemovedOverrideDto),
            itemStatuses = source.ItemStatuses.Select(ItemStatusDto)
        });
    }

    public async Task<JsonElement> CatalogueCategories(CancellationToken cancellationToken)
    {
        context.RequireEnabled();
        if (!context.Management.CategorySelectionEnabled)
        {
            var existingDraft = await state.ReadDraft(context.Binding(), cancellationToken);
            return ProviderJson.Encode(new
            {
                selectionMode = "fixedItemsV1",
                categoryBasis = "primaryCategory",
                maximumSelectedItemCount = MaximumCategoryMenuItems,
                maximumCategoryCount = TenantCatalogueLimits.MaximumCategoryCount,
                maximumItemOverrideCount = TenantCatalogueLimits.MaximumItemOverrides,
                sourceRevision = string.Empty,
                language = string.Empty,
                categories = Array.Empty<object>(),
                sourceChanged = false,
                removedCategoryIds = Array.Empty<Guid>(),
                removedItems = Array.Empty<object>(),
                removedItemOverrides = Array.Empty<object>(),
                itemStatuses = Array.Empty<object>(),
                draftRevision = existingDraft?.Revision,
                draft = (object?)null
            });
        }

        var persisted = await state.ReadDraft(context.Binding(), cancellationToken);
        var snapshot = persisted is not null && TenantCatalogueCategorySnapshot.IsCategorySnapshot(persisted.Snapshot)
            ? TenantCatalogueCategorySnapshot.Read(persisted.Snapshot, persisted.MappingRevision) : null;
        var source = await tenantCatalogue.Categories(context.ConfiguredStore, snapshot?.SourceRevision ?? string.Empty,
            snapshot?.SelectedCategoryIds ?? [], snapshot?.Items.Select(row => new TenantCatalogueItemReference(
                row.ProductId, row.VariationId, row.CategoryId)).ToArray() ?? [],
            snapshot?.ItemOverrides.Select(row => new TenantCatalogueItemOverride(
                row.ProductId, row.VariationId, row.CategoryId, row.Selected)).ToArray() ?? [], cancellationToken);
        var sourceChanged = snapshot is not null && snapshot.SourceRevision != source.Revision;
        var currentCategories = source.Categories.Select(category => CategoryDto(category,
            !sourceChanged && snapshot is not null ? snapshot.Categories.SingleOrDefault(row => row.CategoryId == category.CategoryId) : null))
            .ToArray();
        var draft = persisted is null || snapshot is null ? null : DraftDto(persisted.Revision, snapshot);
        return ProviderJson.Encode(new
        {
            selectionMode = "categoryItemsV1",
            categoryBasis = source.CategoryBasis,
            maximumSelectedItemCount = MaximumCategoryMenuItems,
            maximumCategoryCount = source.MaximumCategoryCount,
            maximumItemOverrideCount = source.MaximumItemOverrideCount,
            sourceRevision = source.Revision,
            language = source.Language,
            categories = currentCategories,
            sourceChanged = sourceChanged || source.SourceChanged,
            removedCategoryIds = source.RemovedCategoryIds,
            removedItems = source.RemovedItems.Select(RemovedOverrideDto),
            removedItemOverrides = source.RemovedItemOverrides.Select(RemovedOverrideDto),
            itemStatuses = source.ItemStatuses.Select(ItemStatusDto),
            draftRevision = persisted?.Revision,
            draft
        });
    }

    private static object RemovedOverrideDto(TenantCatalogueRemovedItemOverride row)
        => new
        {
            selectionKey = row.SelectionKey,
            productId = row.ProductId,
            variationId = row.VariationId,
            categoryId = row.CategoryId,
            currentCategoryId = row.CurrentCategoryId,
            reason = row.Reason
        };

    private static object RemovedOverrideDto(TenantCatalogueRemovedItemReference row)
        => new
        {
            selectionKey = row.SelectionKey,
            productId = row.ProductId,
            variationId = row.VariationId,
            categoryId = row.CategoryId,
            currentCategoryId = row.CurrentCategoryId,
            reason = row.Reason
        };

    private static object ItemStatusDto(TenantCatalogueItemStatus row)
        => new
        {
            selectionKey = row.SelectionKey,
            productId = row.ProductId,
            variationId = row.VariationId,
            categoryId = row.CategoryId,
            currentCategoryId = row.CurrentCategoryId,
            supported = row.Supported
        };

    private static void ValidateCategoryReferences(TenantManagementCategoryChangesRequest request)
    {
        if (request is null || request.CategoryIds is null || request.ItemOverrides is null
            || request.ItemReferences is null || request.ExpectedSourceRevision is null)
            throw new ChannelConsoleException(400, "Refresh the tenant catalogue and review the selected categories and products.");
        if (request.CategoryIds.Count > TenantCatalogueLimits.MaximumCategoryReferences)
            throw new ChannelConsoleException(400, "Review no more than 1,000 categories at a time.", "CategoryLimitExceeded");
        if (request.ItemOverrides.Count > TenantCatalogueLimits.MaximumItemOverrides)
            throw new ChannelConsoleException(400, "Review no more than 2,000 individual item overrides at a time.", "SelectionOverrideLimitExceeded");
        if (request.ItemReferences.Count > TenantCatalogueLimits.SelectionCount
            || request.ItemReferences.Any(row => row is null || row.ProductId == Guid.Empty || row.VariationId == Guid.Empty || row.CategoryId is null)
            || request.ItemReferences.Select(row => (row.ProductId, row.VariationId)).Distinct().Count() != request.ItemReferences.Count
            || request.CategoryIds.Any(id => id == Guid.Empty)
            || request.CategoryIds.Distinct().Count() != request.CategoryIds.Count
            || request.ItemOverrides.Any(row => row is null || row.ProductId == Guid.Empty || row.VariationId == Guid.Empty
                || row.CategoryId is null || row.CategoryId == Guid.Empty)
            || request.ItemOverrides.Select(row => (row.ProductId, row.VariationId)).Distinct().Count() != request.ItemOverrides.Count
            || request.ExpectedSourceRevision.Length is not (0 or TenantCatalogueLimits.RevisionLength)
            || request.ExpectedSourceRevision.Length > 0 && request.ExpectedSourceRevision.Any(c => !char.IsAsciiHexDigitLower(c)))
            throw new ChannelConsoleException(400, "Refresh the tenant catalogue and review the selected categories and products.");
    }

    private static object CategoryDto(TenantCatalogueCategory row, TenantCategoryMapping? selection)
        => new
        {
            categoryId = row.CategoryId,
            name = row.Name,
            displayOrder = row.DisplayOrder,
            totalItemCount = row.TotalItemCount,
            supportedItemCount = row.SupportedItemCount,
            unsupportedItemCount = row.UnsupportedItemCount,
            selectedItemCount = selection?.SelectedItemCount ?? 0,
            selectedUnsupportedItemCount = selection?.SelectedUnsupportedItemCount ?? 0,
            selectionState = SelectionState(selection?.SelectedItemCount ?? 0, row.TotalItemCount),
            active = row.Active
        };

    private static object CategoryDto(TenantCategoryMapping row)
        => new
        {
            categoryId = row.CategoryId,
            name = row.Name,
            displayOrder = row.DisplayOrder,
            totalItemCount = row.TotalItemCount,
            supportedItemCount = row.SupportedItemCount,
            unsupportedItemCount = row.UnsupportedItemCount,
            selectedItemCount = row.SelectedItemCount,
            selectedUnsupportedItemCount = row.SelectedUnsupportedItemCount,
            selectionState = SelectionState(row.SelectedItemCount, row.TotalItemCount),
            active = row.Active
        };

    private static object CategoryOverrideDto(TenantItemMappingOverride row)
        => new
        {
            selectionKey = row.SelectionKey,
            productId = row.ProductId,
            variationId = row.VariationId,
            categoryId = row.CategoryId,
            selected = row.Selected,
            sourceFingerprint = row.SourceFingerprint,
            supported = row.Supported
        };

    internal static object CategoryItemDto(TenantItemMapping row)
        => new
        {
            selectionKey = row.SelectionKey,
            providerItemId = row.ProviderItemId,
            productId = row.ProductId,
            variationId = row.VariationId,
            categoryId = row.CategoryId,
            categoryName = row.CategoryName,
            categoryDisplayOrder = row.CategoryDisplayOrder,
            itemDisplayOrder = row.ItemDisplayOrder,
            name = row.ItemName,
            variationName = row.VariationName,
            description = row.Description,
            priceMinor = row.PriceMinor,
            available = row.Available,
            supported = row.Supported,
            blockReason = row.BlockReason,
            sourceFingerprint = row.SourceFingerprint
        };

    private static object DraftDto(string revision, TenantCatalogueCategoryDraftSnapshot snapshot)
        => new
        {
            draftRevision = revision,
            sourceRevision = snapshot.SourceRevision,
            language = snapshot.Language,
            selectedCategoryIds = snapshot.SelectedCategoryIds,
            itemOverrides = snapshot.ItemOverrides.Select(CategoryOverrideDto),
            categories = snapshot.Categories.OrderBy(row => row.DisplayOrder).ThenBy(row => row.CategoryId).Select(CategoryDto),
            items = snapshot.Items.OrderBy(row => row.CategoryDisplayOrder).ThenBy(row => row.ItemDisplayOrder)
                .ThenBy(row => row.ProductId).ThenBy(row => row.VariationId).Select(CategoryItemDto)
        };

    private static string SelectionState(int selected, int total)
        => selected == 0 ? "empty" : selected == total ? "all" : "partial";
}
