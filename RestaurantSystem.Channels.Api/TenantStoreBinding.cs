namespace RestaurantSystem.Channels.Api;

/// <summary>Deployment-owned, reviewed published-catalogue mapping. Never populated from a webhook.</summary>
public sealed class TenantStoreBinding
{
    public Guid StoreId { get; set; }
    public string TenantId { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = string.Empty;
    public string ApiToken { get; set; } = string.Empty;
    public string Currency { get; set; } = string.Empty;
    public string CatalogueRevision { get; set; } = string.Empty;
    public string PublishedMenuHash { get; set; } = string.Empty;
    public List<TenantItemMapping> Items { get; set; } = [];
}

public sealed class TenantItemMapping
{
    public string ProviderItemId { get; set; } = string.Empty;
    public Guid ProductId { get; set; }
    public Guid? VariationId { get; set; }
    public string? VariationName { get; set; }
}
