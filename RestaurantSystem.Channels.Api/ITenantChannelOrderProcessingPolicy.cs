using RestaurantSystem.Channels.Domain;

namespace RestaurantSystem.Channels.Api;

public sealed record CreatedOrderRecoveryContext(TenantStoreBinding Store, string ClientId, int ListLimit,
    DateTimeOffset EnrollmentStartedAt);

public sealed record TenantOrderImportContext(TenantStoreBinding Store, string ClientId, DateTimeOffset EnrollmentStartedAt,
    int PayloadRetentionDays, int RetrySeconds);

public interface ITenantChannelOrderProcessingPolicy
{
    Task<CreatedOrderRecoveryContext?> ResolveRecovery(CancellationToken cancellationToken);
    Task<TenantOrderImportContext?> ResolveImport(CancellationToken cancellationToken);
    Task<TenantStoreBinding> HistoricalStore(TenantOrderImportContext context, string catalogueRevision,
        CancellationToken cancellationToken);
}
