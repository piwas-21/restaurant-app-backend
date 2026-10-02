namespace RestaurantSystem.Channels.Api;

public interface ICatalogueMappingResolver
{
    Task<TenantStoreBinding> Active(CancellationToken cancellationToken);
    Task<TenantStoreBinding> ForRevision(string catalogueRevision, CancellationToken cancellationToken);
}
