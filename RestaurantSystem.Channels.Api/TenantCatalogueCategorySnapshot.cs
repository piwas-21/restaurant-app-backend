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
        if (snapshot.SchemaVersion != Version || snapshot.CatalogueRevision != catalogueRevision
            || snapshot.CategoryBasis != "primaryCategory"
            || !Revision(snapshot.SourceRevision) || snapshot.Language is not ("en" or "nl" or "fr" or "de" or "tr" or "ar")
            || snapshot.Categories is null || snapshot.Items is null || snapshot.SelectedCategoryIds is null
            || snapshot.ItemOverrides is null || snapshot.Categories.Count > TenantCatalogueLimits.MaximumCategoryCount
            || snapshot.Items.Count is < 1 or > TenantCatalogueLimits.SelectionCount)
            return false;
        var categories = snapshot.Categories;
        if (categories.Any(row => row is null) || snapshot.Items.Any(row => row is null)
            || snapshot.ItemOverrides.Any(row => row is null) || categories.Select(row => row.CategoryId).Distinct().Count() != categories.Count
            || categories.Any(row => row.CategoryId == Guid.Empty || row.ProviderCategoryId != TenantCatalogueSelectionIds.Category(row.CategoryId)
                || string.IsNullOrEmpty(row.Name) || row.Name.Length > 200 || row.Name.Any(char.IsControl) || row.DisplayOrder < 0
                || row.TotalItemCount < 0 || row.SupportedItemCount < 0 || row.UnsupportedItemCount < 0
                || row.SupportedItemCount + row.UnsupportedItemCount != row.TotalItemCount
                || row.SelectedItemCount < 0 || row.SelectedItemCount > row.TotalItemCount
                || row.SelectedUnsupportedItemCount < 0 || row.SelectedUnsupportedItemCount > row.SelectedItemCount)) return false;
        var categoryById = categories.ToDictionary(row => row.CategoryId);
        if (snapshot.Items.Select(row => row.ProviderItemId).Distinct(StringComparer.Ordinal).Count() != snapshot.Items.Count
            || snapshot.Items.Select(row => (row.ProductId, row.VariationId)).Distinct().Count() != snapshot.Items.Count
            || snapshot.Items.Any(row => row.ProductId == Guid.Empty || row.VariationId == Guid.Empty
                || row.ProviderItemId != TenantCatalogueSelectionIds.Item(row.ProductId, row.VariationId)
                || row.SelectionKey is null || row.SelectionKey != TenantCatalogueSelectionIds.SelectionKey(row.ProductId, row.VariationId)
                || row.SourceFingerprint is null || row.SourceFingerprint.Length != TenantCatalogueLimits.RevisionLength || !Revision(row.SourceFingerprint)
                || string.IsNullOrEmpty(row.ItemName) || row.ItemName.Length > TenantCatalogueLimits.ItemNameLength || row.ItemName.Any(char.IsControl)
                || row.Description is null || row.Description.Length > TenantCatalogueLimits.DescriptionLength || row.Description.Any(char.IsControl)
                || row.CategoryId is null || !categoryById.TryGetValue(row.CategoryId.Value, out var category)
                    || category.Name != row.CategoryName || category.DisplayOrder != row.CategoryDisplayOrder
                || row.CategoryId.HasValue != (row.CategoryName is not null)
                || row.CategoryId.HasValue != row.CategoryDisplayOrder.HasValue
                || row.BlockReason is null || row.Supported != (row.BlockReason.Length == 0) || row.BlockReason.Length > 80
                || row.BlockReason.Any(char.IsControl) || row.Supported && row.PriceMinor is null)) return false;
        var counts = snapshot.Items.Where(row => row.CategoryId.HasValue).GroupBy(row => row.CategoryId!.Value)
            .ToDictionary(group => group.Key, group => (Total: group.Count(), Blocked: group.Count(row => !row.Supported)));
        if (categories.Any(row => row.SelectedItemCount != counts.GetValueOrDefault(row.CategoryId).Total
            || row.SelectedUnsupportedItemCount != counts.GetValueOrDefault(row.CategoryId).Blocked)) return false;
        if (snapshot.SelectedCategoryIds.Distinct().Count() != snapshot.SelectedCategoryIds.Count
            || snapshot.SelectedCategoryIds.Any(id => !categoryById.ContainsKey(id))) return false;
        if (snapshot.ItemOverrides.Count > TenantCatalogueLimits.MaximumItemOverrides
            || snapshot.ItemOverrides.Select(row => (row.ProductId, row.VariationId)).Distinct().Count() != snapshot.ItemOverrides.Count
            || snapshot.ItemOverrides.Any(row => row.ProductId == Guid.Empty || row.VariationId == Guid.Empty
                || row.CategoryId is null || !categoryById.ContainsKey(row.CategoryId.Value)
                || row.SelectionKey is null || row.SelectionKey != TenantCatalogueSelectionIds.SelectionKey(row.ProductId, row.VariationId)
                || row.SourceFingerprint is null
                || row.SourceFingerprint.Length != TenantCatalogueLimits.RevisionLength || !Revision(row.SourceFingerprint)
                || row.Selected == snapshot.SelectedCategoryIds.Contains(row.CategoryId.Value))) return false;
        var selectedByIdentity = snapshot.Items.ToDictionary(row => (row.ProductId, row.VariationId));
        if (snapshot.ItemOverrides.Any(row => row.Selected
            && (!selectedByIdentity.TryGetValue((row.ProductId, row.VariationId), out var item)
                || item.CategoryId != row.CategoryId || item.SourceFingerprint != row.SourceFingerprint
                || item.Supported != row.Supported))) return false;
        return true;
    }

    private static bool Revision(string? value)
        => value is { Length: TenantCatalogueLimits.RevisionLength } && value.All(char.IsAsciiHexDigitLower);

    private static ChannelConsoleException Invalid()
        => new(409, "The saved category selection needs operator review.");
}
