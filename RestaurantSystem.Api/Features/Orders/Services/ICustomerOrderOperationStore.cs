namespace RestaurantSystem.Api.Features.Orders.Services;

public enum CustomerOrderOperationOutcome { Unseen, Applied, Cancelled }
public sealed record CustomerOrderOperationResolution(CustomerOrderOperationOutcome Outcome, Guid? OrderId);

/// <summary>
/// Owns the transaction and shared basket-owner lock. Entry requires a clean change tracker;
/// effects use the same scoped DbContext. Failure detaches all transaction state after rollback.
/// </summary>
public interface ICustomerOrderOperationStore
{
    Task<CustomerOrderOperationResolution> ExecuteAsync(string sessionId, Guid operationId, string payloadHash,
        Func<Task<Guid>> accept, CancellationToken cancellationToken);
    Task<CustomerOrderOperationResolution> RecoverAsync(string sessionId, Guid operationId, string payloadHash,
        CancellationToken cancellationToken);
    Task<CustomerOrderOperationResolution> CancelAsync(string sessionId, Guid operationId, string payloadHash,
        CancellationToken cancellationToken);
}
