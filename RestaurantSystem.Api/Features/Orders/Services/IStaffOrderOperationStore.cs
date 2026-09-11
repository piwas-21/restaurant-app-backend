using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.Orders.Services;

public enum StaffOrderOperationReplayOutcome
{
    None,
    Replay,
    PayloadMismatch,
    OperationIdReused
}

public sealed record StaffOrderOperationReplay(
    StaffOrderOperationReplayOutcome Outcome, StaffOrderOperation? Operation, Order? Order);

public interface IStaffOrderOperationStore
{
    Task<StaffOrderOperationReplay> ResolveAsync(
        Guid operationId, StaffOrderOperationKind kind, Guid? orderId,
        Guid? actorUserId, string payloadHash, CancellationToken cancellationToken);

    Task<Order?> LoadOrderAsync(Guid orderId, CancellationToken cancellationToken);
}
