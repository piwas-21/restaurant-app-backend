namespace RestaurantSystem.Channels.Api;

public sealed record TenantCatalogueSnapshot(string Revision, IReadOnlyList<TenantCatalogueItem> Items)
{
    public string Language { get; init; } = string.Empty;
    public IReadOnlyList<TenantCatalogueSelectionCategory> Categories { get; init; } = [];
}

public sealed record TenantCatalogueItem(Guid ProductId, Guid? VariationId, string Name, string Description,
    string? VariationName, int? PriceMinor, bool Available, string BlockReason)
{
    public string SelectionKey { get; init; } = string.Empty;
    public Guid? CategoryId { get; init; }
    public string? CategoryName { get; init; }
    public int? CategoryDisplayOrder { get; init; }
    public int ItemDisplayOrder { get; init; }
    public string SourceFingerprint { get; init; } = string.Empty;
}

public sealed record TenantCatalogueCategory(Guid CategoryId, string Name, int DisplayOrder,
    int TotalItemCount, int SupportedItemCount, int UnsupportedItemCount, bool Active);

public sealed record TenantCatalogueCategories(string Revision, string Language,
    IReadOnlyList<TenantCatalogueCategory> Categories)
{
    public string CategoryBasis { get; init; } = "primaryCategory";
    public int MaximumCategoryCount { get; init; } = TenantCatalogueLimits.MaximumCategoryCount;
    public int MaximumItemOverrideCount { get; init; } = TenantCatalogueLimits.MaximumItemOverrides;
    public bool SourceChanged { get; init; }
    public IReadOnlyList<Guid> RemovedCategoryIds { get; init; } = [];
    public IReadOnlyList<TenantCatalogueRemovedItemReference> RemovedItems { get; init; } = [];
    public IReadOnlyList<TenantCatalogueRemovedItemOverride> RemovedItemOverrides { get; init; } = [];
    public IReadOnlyList<TenantCatalogueItemStatus> ItemStatuses { get; init; } = [];
}
