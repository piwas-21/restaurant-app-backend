using System.Text.Json;

namespace RestaurantSystem.Channels.Api;

internal sealed record TenantCatalogueCategoryDraftSnapshot(int SchemaVersion, string CatalogueRevision,
    string CategoryBasis, string SourceRevision, string Language, IReadOnlyList<Guid> SelectedCategoryIds,
    IReadOnlyList<TenantItemMappingOverride> ItemOverrides,
    IReadOnlyList<TenantCategoryMapping> Categories, IReadOnlyList<TenantItemMapping> Items);

internal static class TenantCatalogueCategorySnapshot
{
    private const int Version = 2;
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static JsonElement Create(string catalogueRevision, string sourceRevision, string language,
        IReadOnlyList<Guid> selectedCategoryIds, IReadOnlyList<TenantItemMappingOverride> itemOverrides,
        IReadOnlyList<TenantCategoryMapping> categories, IReadOnlyList<TenantItemMapping> items)
        => JsonSerializer.SerializeToElement(new TenantCatalogueCategoryDraftSnapshot(Version, catalogueRevision,
            "primaryCategory", sourceRevision, language, selectedCategoryIds, itemOverrides, categories, items), Options);

    public static bool IsCategorySnapshot(JsonElement snapshot)
        => snapshot.ValueKind == JsonValueKind.Object && snapshot.TryGetProperty("schemaVersion", out var version)
            && version.ValueKind == JsonValueKind.Number && version.TryGetInt32(out var value) && value == Version;

    public static JsonElement ForPublication(TenantStoreBinding store)
        => Create(store.CatalogueRevision, store.SourceRevision, store.Language, store.SelectedCategoryIds,
            store.ItemOverrides, store.Categories, store.Items);

    public static TenantCatalogueCategoryDraftSnapshot Read(JsonElement snapshot, string catalogueRevision)
    {
        TenantCatalogueCategoryDraftSnapshot? value;
        try { value = JsonSerializer.Deserialize<TenantCatalogueCategoryDraftSnapshot>(snapshot, Options); }
        catch (JsonException) { throw Invalid(); }
        if (value is null || !Valid(value, catalogueRevision)) throw Invalid();
        return value;
    }

    private static bool Valid(TenantCatalogueCategoryDraftSnapshot snapshot, string catalogueRevision)
    {
        return ValidHeader(snapshot, catalogueRevision)
            && ValidCategories(snapshot.Categories)
            && ValidItems(snapshot.Items, snapshot.Categories)
            && ValidCategoryCounts(snapshot.Items, snapshot.Categories)
            && ValidSelection(snapshot);
    }

    private static bool ValidHeader(TenantCatalogueCategoryDraftSnapshot snapshot, string catalogueRevision)
        => snapshot.SchemaVersion == Version && snapshot.CatalogueRevision == catalogueRevision
            && snapshot.CategoryBasis == "primaryCategory" && Revision(snapshot.SourceRevision)
            && snapshot.Language is "en" or "nl" or "fr" or "de" or "tr" or "ar"
            && snapshot.Categories is not null && snapshot.Categories.Count <= TenantCatalogueLimits.MaximumCategoryCount
            && snapshot.Items is { Count: >= 1 and <= TenantCatalogueLimits.SelectionCount }
            && snapshot.SelectedCategoryIds is not null && snapshot.ItemOverrides is not null;

    private static bool ValidCategories(IReadOnlyList<TenantCategoryMapping> categories)
        => !categories.Any(row => row is null)
            && categories.Select(row => row.CategoryId).Distinct().Count() == categories.Count
            && categories.All(ValidCategory);

    private static bool ValidCategory(TenantCategoryMapping row)
        => row.CategoryId != Guid.Empty && row.ProviderCategoryId == TenantCatalogueSelectionIds.Category(row.CategoryId)
            && !string.IsNullOrEmpty(row.Name) && row.Name.Length <= 200 && !row.Name.Any(char.IsControl)
            && row.DisplayOrder >= 0 && row.TotalItemCount >= 0 && row.SupportedItemCount >= 0 && row.UnsupportedItemCount >= 0
            && (long)row.SupportedItemCount + row.UnsupportedItemCount == row.TotalItemCount
            && row.SelectedItemCount >= 0 && row.SelectedItemCount <= row.TotalItemCount
            && row.SelectedUnsupportedItemCount >= 0 && row.SelectedUnsupportedItemCount <= row.SelectedItemCount;

    private static bool ValidItems(IReadOnlyList<TenantItemMapping> items, IReadOnlyList<TenantCategoryMapping> categories)
    {
        if (items.Any(row => row is null)
            || items.Select(row => row.ProviderItemId).Distinct(StringComparer.Ordinal).Count() != items.Count
            || items.Select(row => (row.ProductId, row.VariationId)).Distinct().Count() != items.Count) return false;
        var categoryById = categories.ToDictionary(row => row.CategoryId);
        return items.All(row => ValidItem(row, categoryById));
    }

    private static bool ValidItem(TenantItemMapping row, Dictionary<Guid, TenantCategoryMapping> categories)
    {
        if (row.ProductId == Guid.Empty || row.VariationId == Guid.Empty
            || row.ProviderItemId != TenantCatalogueSelectionIds.Item(row.ProductId, row.VariationId)
            || row.SelectionKey != TenantCatalogueSelectionIds.SelectionKey(row.ProductId, row.VariationId)
            || !ValidRevision(row.SourceFingerprint) || string.IsNullOrEmpty(row.ItemName)
            || row.ItemName.Length > TenantCatalogueLimits.ItemNameLength || row.ItemName.Any(char.IsControl)
            || row.Description is null || row.Description.Length > TenantCatalogueLimits.DescriptionLength
            || row.Description.Any(char.IsControl) || row.CategoryId is not { } categoryId
            || !categories.TryGetValue(categoryId, out var category) || category.Name != row.CategoryName
            || category.DisplayOrder != row.CategoryDisplayOrder || row.CategoryName is null
            || row.CategoryDisplayOrder is null || row.BlockReason is null
            || row.Supported != (row.BlockReason.Length == 0) || row.BlockReason.Length > 80
            || row.BlockReason.Any(char.IsControl) || row.Supported && row.PriceMinor is null) return false;
        return true;
    }

    private static bool ValidCategoryCounts(IReadOnlyList<TenantItemMapping> items,
        IReadOnlyList<TenantCategoryMapping> categories)
    {
        var counts = items.GroupBy(row => row.CategoryId!.Value)
            .ToDictionary(group => group.Key, group => (Total: group.Count(), Blocked: group.Count(row => !row.Supported)));
        return categories.All(row => row.SelectedItemCount == counts.GetValueOrDefault(row.CategoryId).Total
            && row.SelectedUnsupportedItemCount == counts.GetValueOrDefault(row.CategoryId).Blocked);
    }

    private static bool ValidSelection(TenantCatalogueCategoryDraftSnapshot snapshot)
    {
        var categoryIds = snapshot.Categories.Select(row => row.CategoryId).ToHashSet();
        if (snapshot.SelectedCategoryIds.Distinct().Count() != snapshot.SelectedCategoryIds.Count
            || snapshot.SelectedCategoryIds.Any(id => !categoryIds.Contains(id))) return false;
        return ValidOverrides(snapshot, categoryIds);
    }

    private static bool ValidOverrides(TenantCatalogueCategoryDraftSnapshot snapshot, HashSet<Guid> categoryIds)
    {
        var overrides = snapshot.ItemOverrides;
        if (overrides.Any(row => row is null) || overrides.Count > TenantCatalogueLimits.MaximumItemOverrides
            || overrides.Select(row => (row.ProductId, row.VariationId)).Distinct().Count() != overrides.Count
            || overrides.Any(row => row.ProductId == Guid.Empty || row.VariationId == Guid.Empty
                || row.CategoryId is not { } categoryId || !categoryIds.Contains(categoryId)
                || row.SelectionKey != TenantCatalogueSelectionIds.SelectionKey(row.ProductId, row.VariationId)
                || !ValidRevision(row.SourceFingerprint)
                || row.Selected == snapshot.SelectedCategoryIds.Contains(categoryId))) return false;
        var selectedByIdentity = snapshot.Items.ToDictionary(row => (row.ProductId, row.VariationId));
        return overrides.Where(row => row.Selected).All(row =>
            selectedByIdentity.TryGetValue((row.ProductId, row.VariationId), out var item)
            && item.CategoryId == row.CategoryId && item.SourceFingerprint == row.SourceFingerprint
            && item.Supported == row.Supported);
    }

    private static bool ValidRevision(string? value)
        => value is { Length: TenantCatalogueLimits.RevisionLength } && value.All(char.IsAsciiHexDigitLower);

    private static bool Revision(string? value)
        => ValidRevision(value);

    private static ChannelConsoleException Invalid()
        => new(409, "The saved category selection needs operator review.");
}
