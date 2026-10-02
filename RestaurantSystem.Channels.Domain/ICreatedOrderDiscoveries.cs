namespace RestaurantSystem.Channels.Domain;

public interface ICreatedOrderDiscoveries
{
    Task Record(string clientId, Guid storeId, string tenantId, string catalogueRevision, DateTimeOffset enrolledAt,
        IReadOnlyList<CreatedOrderCandidate> orders, CancellationToken cancellationToken);
}
