namespace RestaurantSystem.Channels.Domain;

public interface IChannelObservationJobs
{
    Task<ChannelObservationJob?> Claim(string clientId, Guid storeId, string tenantId, CancellationToken cancellationToken);
    Task<bool> Finish(ChannelObservationJob job, string? canonicalState, string? canonicalHash,
        DateTimeOffset? observedAt, bool terminal, DateTimeOffset availableAt, CancellationToken cancellationToken);
}
