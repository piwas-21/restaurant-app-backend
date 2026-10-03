namespace RestaurantSystem.Channels.Api;

public interface ITenantCatalogueClient
{
    Task<TenantCatalogueSnapshot> Read(TenantStoreBinding store, CancellationToken cancellationToken);
    Task<TenantCatalogueCategories> Categories(TenantStoreBinding store, string expectedSourceRevision,
        IReadOnlyList<Guid> categoryIds, IReadOnlyList<TenantCatalogueItemReference> itemReferences,
        IReadOnlyList<TenantCatalogueItemOverride> overrides,
        CancellationToken cancellationToken);
    Task<TenantCatalogueSelection> ReadSelection(TenantStoreBinding store, string expectedSourceRevision,
        IReadOnlyList<Guid> categoryIds, IReadOnlyList<TenantCatalogueItemOverride> overrides,
        CancellationToken cancellationToken);
}
