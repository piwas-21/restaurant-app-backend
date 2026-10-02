namespace RestaurantSystem.Channels.Domain;

public sealed record TenantOAuthFlow(Guid Id, string ClientId, Guid StoreId, string TenantId, Guid ActorId,
    string StateHash, string VerifierCipher, bool EnableOrderAcceptance, string Status, string? ErrorCode,
    DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt, DateTimeOffset? CompletedAt);

public interface ITenantOAuthFlows
{
    Task Create(TenantOAuthFlow flow, CancellationToken cancellationToken);
    Task<TenantOAuthFlow?> Read(AvailabilityBinding binding, Guid id, CancellationToken cancellationToken);
    Task<TenantOAuthFlow?> FindByStateHash(AvailabilityBinding binding, string stateHash, CancellationToken cancellationToken);
    Task Expire(AvailabilityBinding binding, Guid id, DateTimeOffset now, CancellationToken cancellationToken);
    Task<TenantOAuthFlow?> Claim(AvailabilityBinding binding, string stateHash, DateTimeOffset now, CancellationToken cancellationToken);
    Task<bool> FailPending(AvailabilityBinding binding, Guid id, string stateHash, string errorCode,
        DateTimeOffset completedAt, CancellationToken cancellationToken);
    Task<int> CancelPending(AvailabilityBinding binding, DateTimeOffset now, CancellationToken cancellationToken);
    Task<bool> Finish(AvailabilityBinding binding, Guid id, string status, string? errorCode,
        DateTimeOffset completedAt, CancellationToken cancellationToken);
}
