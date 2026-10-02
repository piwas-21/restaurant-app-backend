using RestaurantSystem.Channels.Domain;

namespace RestaurantSystem.Channels.Tests;

// Freeze a persisted failure while a new attempt takes ownership; expose the historical race deterministically.
public sealed class DecisionBarrierRepository(IConsoleRepository inner) : IConsoleRepository
{
    public TaskCompletionSource FailedWritten { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource ReleaseFailedWriter { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task CreateSession(string hash, DateTimeOffset expiresAt, CancellationToken cancellationToken) => inner.CreateSession(hash, expiresAt, cancellationToken);
    public Task<bool> SessionExists(string hash, DateTimeOffset now, CancellationToken cancellationToken) => inner.SessionExists(hash, now, cancellationToken);
    public Task EndSession(string hash, CancellationToken cancellationToken) => inner.EndSession(hash, cancellationToken);
    public Task SaveAuthorization(AuthorizationState state, CancellationToken cancellationToken) => inner.SaveAuthorization(state, cancellationToken);
    public Task<AuthorizationState?> ClaimAuthorization(string hash, string session, DateTimeOffset now, CancellationToken cancellationToken) => inner.ClaimAuthorization(hash, session, now, cancellationToken);
    public Task SaveToken(StoredToken token, CancellationToken cancellationToken) => inner.SaveToken(token, cancellationToken);
    public Task<StoredToken?> FindToken(string clientId, Guid storeId, string kind, CancellationToken cancellationToken) => inner.FindToken(clientId, storeId, kind, cancellationToken);
    public Task<IReadOnlyList<WebhookReceipt>> RecentReceipts(string clientId, Guid storeId, CancellationToken cancellationToken) => inner.RecentReceipts(clientId, storeId, cancellationToken);
    public Task<bool> HasOrder(string clientId, Guid storeId, string orderId, CancellationToken cancellationToken) => inner.HasOrder(clientId, storeId, orderId, cancellationToken);
    public Task<DateTimeOffset?> RecoveryEnrollment(string clientId, Guid storeId, Guid orderId, CancellationToken cancellationToken) => inner.RecoveryEnrollment(clientId, storeId, orderId, cancellationToken);
    public Task<bool> ClaimAction(string clientId, Guid storeId, string orderId, string action, CancellationToken cancellationToken) => inner.ClaimAction(clientId, storeId, orderId, action, cancellationToken);
    public Task<OrderAction?> FindAction(string clientId, Guid storeId, string orderId, CancellationToken cancellationToken) => inner.FindAction(clientId, storeId, orderId, cancellationToken);
    public async Task FinishAction(string clientId, Guid storeId, string orderId, string state, CancellationToken cancellationToken)
    {
        await inner.FinishAction(clientId, storeId, orderId, state, cancellationToken);
        if (state != "Failed") return;
        FailedWritten.SetResult();
        await ReleaseFailedWriter.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
    }
}
