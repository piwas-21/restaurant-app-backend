using System.Text.Json.Serialization;

namespace RestaurantSystem.Channels.Api;

public sealed record TenantCatalogueItemOverride(Guid ProductId, Guid? VariationId, Guid? CategoryId, bool Selected)
{
    public string SelectionKey { get; init; } = string.Empty;
    public string SourceFingerprint { get; init; } = string.Empty;
    public bool Supported { get; init; }
}

public sealed record TenantCatalogueSelectionCategory(Guid CategoryId, string Name, int DisplayOrder,
    int TotalItemCount, int SupportedItemCount, int UnsupportedItemCount, int SelectedItemCount,
    int SelectedUnsupportedItemCount, bool Active);

public sealed record TenantCatalogueSelectionItem(string SelectionKey, Guid ProductId, Guid? VariationId,
    Guid? CategoryId, string? CategoryName, int? CategoryDisplayOrder, int ItemDisplayOrder,
    string Name, string Description, string? VariationName, int? PriceMinor, bool Available, bool Supported,
    string BlockReason, string SourceFingerprint);

public sealed record TenantCatalogueItemReference([property: JsonRequired] Guid ProductId,
    [property: JsonRequired] Guid? VariationId, [property: JsonRequired] Guid? CategoryId);

public sealed record TenantCatalogueRemovedItemReference(string SelectionKey, Guid ProductId, Guid? VariationId,
    Guid CategoryId, Guid? CurrentCategoryId, string Reason);

public sealed record TenantCatalogueRemovedItemOverride(string SelectionKey, Guid ProductId, Guid? VariationId,
    Guid CategoryId, Guid? CurrentCategoryId, string Reason);

public sealed record TenantCatalogueItemStatus(string SelectionKey, Guid ProductId, Guid? VariationId,
    Guid CategoryId, Guid? CurrentCategoryId, bool Supported);

public sealed record TenantCatalogueSelection(string Revision, string Language,
    IReadOnlyList<TenantCatalogueSelectionCategory> Categories, IReadOnlyList<Guid> SelectedCategoryIds,
    IReadOnlyList<TenantCatalogueItemOverride> ItemOverrides, IReadOnlyList<TenantCatalogueSelectionItem> Items);
