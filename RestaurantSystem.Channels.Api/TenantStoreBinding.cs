namespace RestaurantSystem.Channels.Api;

/// <summary>Deployment-owned, reviewed published-catalogue mapping. Never populated from a webhook.</summary>
public sealed class TenantStoreBinding
{
    public Guid StoreId { get; set; }
    public string TenantId { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = string.Empty;
    public string ApiToken { get; set; } = string.Empty;
    public string CatalogueApiToken { get; set; } = string.Empty;
    public string Currency { get; set; } = string.Empty;
    public string CatalogueRevision { get; set; } = string.Empty;
    public string PublishedMenuHash { get; set; } = string.Empty;
    public string SourceRevision { get; set; } = string.Empty;
    public string Language { get; set; } = string.Empty;
    public List<Guid> SelectedCategoryIds { get; set; } = [];
    public List<TenantItemMappingOverride> ItemOverrides { get; set; } = [];
    public List<TenantItemMapping> Items { get; set; } = [];
    public List<TenantCategoryMapping> Categories { get; set; } = [];
}

public sealed class TenantItemMapping
{
    public string ProviderItemId { get; set; } = string.Empty;
    public Guid ProductId { get; set; }
    public Guid? VariationId { get; set; }
    public string? VariationName { get; set; }
    public string SelectionKey { get; set; } = string.Empty;
    public Guid? CategoryId { get; set; }
    public string? CategoryName { get; set; }
    public int? CategoryDisplayOrder { get; set; }
    public int ItemDisplayOrder { get; set; }
    public string SourceFingerprint { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int? PriceMinor { get; set; }
    public bool Available { get; set; }
    public bool Supported { get; set; }
    public string BlockReason { get; set; } = string.Empty;
}

public sealed class TenantCategoryMapping
{
    public Guid CategoryId { get; set; }
    public string ProviderCategoryId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int DisplayOrder { get; set; }
    public bool Active { get; set; }
    public int TotalItemCount { get; set; }
    public int SupportedItemCount { get; set; }
    public int UnsupportedItemCount { get; set; }
    public int SelectedItemCount { get; set; }
    public int SelectedUnsupportedItemCount { get; set; }
}

public sealed class TenantItemMappingOverride
{
    public Guid ProductId { get; set; }
    public Guid? VariationId { get; set; }
    public Guid? CategoryId { get; set; }
    public bool Selected { get; set; }
    public string SelectionKey { get; set; } = string.Empty;
    public string SourceFingerprint { get; set; } = string.Empty;
    public bool Supported { get; set; }
}

public sealed record TenantCatalogueBindingSelection(string SourceRevision, string Language,
    IReadOnlyList<Guid> SelectedCategoryIds, IReadOnlyList<TenantItemMappingOverride> ItemOverrides,
    IReadOnlyList<TenantCategoryMapping> Categories);
