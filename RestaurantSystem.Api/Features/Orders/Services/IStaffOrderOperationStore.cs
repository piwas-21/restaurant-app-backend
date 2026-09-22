using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.Orders.Services;

public enum StaffOrderOperationReplayOutcome
{
    None,
    Replay,
    PayloadMismatch,
    OperationIdReused,
    Unknown
}

public sealed record StaffOrderOperationReplay(
    StaffOrderOperationReplayOutcome Outcome, StaffOrderOperation? Operation, Order? Order);

public interface IStaffOrderOperationStore
{
    Task<StaffOrderOperationReplay> ResolveAsync(
        Guid operationId, StaffOrderOperationKind kind, Guid? orderId,
        Guid? actorUserId, string payloadHash, CancellationToken cancellationToken);

    Task<StaffOrderOperationReplay> LookupAsync(
        Guid operationId, StaffOrderOperationKind kind, Guid? actorUserId,
        CancellationToken cancellationToken);

    Task<Order?> LoadOrderAsync(Guid orderId, CancellationToken cancellationToken);
}
