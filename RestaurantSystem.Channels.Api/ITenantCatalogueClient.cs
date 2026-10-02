using System.Text.Json;

namespace RestaurantSystem.Channels.Api;

public sealed record TenantCatalogueSnapshot(string Revision, IReadOnlyList<TenantCatalogueItem> Items);
public sealed record TenantCatalogueItem(Guid ProductId, Guid? VariationId, string Name, string Description,
    string? VariationName, int? PriceMinor, bool Available, string BlockReason);

public interface ITenantCatalogueClient
{
    Task<TenantCatalogueSnapshot> Read(TenantStoreBinding store, CancellationToken cancellationToken);
}
