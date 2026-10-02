namespace RestaurantSystem.Channels.Api;

public sealed record TenantImportResult(Guid OrderId, bool AlreadyImported);

public interface ITenantOrderClient
{
    Task<TenantImportResult> Import(TenantStoreBinding store, TenantOrderRequest order, CancellationToken cancellationToken);
}
