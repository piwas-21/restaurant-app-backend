using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.TableServiceSessions.Services;

public interface ITableBillPaymentSessionCoordinator
{
    Task<TableServiceSession?> LockOpenAsync(Guid? serviceSessionId, CancellationToken cancellationToken);
    Task CompleteAsync(
        TableServiceSession? session, Guid paymentOperationKey, CancellationToken cancellationToken);
}
