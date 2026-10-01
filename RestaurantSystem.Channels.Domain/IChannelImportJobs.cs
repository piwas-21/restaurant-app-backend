namespace RestaurantSystem.Channels.Domain;

public interface IChannelImportJobs
{
    Task ExpirePayloads(CancellationToken cancellationToken);
    Task Discover(string clientId, Guid storeId, string tenantId, string revision, DateTimeOffset enrolledAt, CancellationToken cancellationToken);
    Task<ChannelImportJob?> Claim(string clientId, Guid storeId, string tenantId, CancellationToken cancellationToken);
    Task<bool> Prepare(ChannelImportJob job, string ciphertext, string requestHash, DateTimeOffset expiresAt, CancellationToken cancellationToken);
    Task<bool> Imported(ChannelImportJob job, Guid tenantOrderId, CancellationToken cancellationToken);
    Task<bool> Defer(ChannelImportJob job, string code, DateTimeOffset availableAt, bool quarantine, CancellationToken cancellationToken);
}
