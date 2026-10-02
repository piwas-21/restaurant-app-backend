namespace RestaurantSystem.Channels.Api;

public interface ITenantDecisionClient
{
    Task<TenantDecisionLease?> Claim(TenantStoreBinding store, CancellationToken cancellationToken);
    Task Report(TenantStoreBinding store, TenantDecisionLease lease, TenantDecisionReport report, CancellationToken cancellationToken);
}
