namespace RestaurantSystem.Channels.Domain;

public interface IConsoleRepository
{
    Task CreateSession(string hash, DateTimeOffset expiresAt, CancellationToken cancellationToken);
    Task<bool> SessionExists(string hash, DateTimeOffset now, CancellationToken cancellationToken);
    Task EndSession(string hash, CancellationToken cancellationToken);
    Task SaveAuthorization(AuthorizationState state, CancellationToken cancellationToken);
    Task<AuthorizationState?> ClaimAuthorization(string hash, string session, DateTimeOffset now, CancellationToken cancellationToken);
    Task SaveToken(StoredToken token, CancellationToken cancellationToken);
    Task<StoredToken?> FindToken(string clientId, Guid storeId, string kind, CancellationToken cancellationToken);
    Task<IReadOnlyList<WebhookReceipt>> RecentReceipts(string clientId, Guid storeId, CancellationToken cancellationToken);
    Task<bool> HasOrder(string clientId, Guid storeId, string orderId, CancellationToken cancellationToken);
    Task<DateTimeOffset?> RecoveryEnrollment(string clientId, Guid storeId, Guid orderId, CancellationToken cancellationToken);
    Task<bool> ClaimAction(string clientId, Guid storeId, string orderId, string action, CancellationToken cancellationToken);
    Task FinishAction(string clientId, Guid storeId, string orderId, string state, CancellationToken cancellationToken);
    Task<OrderAction?> FindAction(string clientId, Guid storeId, string orderId, CancellationToken cancellationToken);
}
