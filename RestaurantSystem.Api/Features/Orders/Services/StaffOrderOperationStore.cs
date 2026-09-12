using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Features.Orders.Queries;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Orders.Services;

public sealed class StaffOrderOperationStore : IStaffOrderOperationStore
{
    private readonly ApplicationDbContext _context;

    public StaffOrderOperationStore(ApplicationDbContext context) => _context = context;

    public async Task<StaffOrderOperationReplay> ResolveAsync(
        Guid operationId, StaffOrderOperationKind kind, Guid? orderId,
        Guid? actorUserId, string payloadHash, CancellationToken cancellationToken)
    {
        var operation = await _context.StaffOrderOperations.AsNoTracking()
            .SingleOrDefaultAsync(item => item.OperationId == operationId, cancellationToken);
        if (operation == null)
        {
            return new StaffOrderOperationReplay(StaffOrderOperationReplayOutcome.None, null, null);
        }

        if (operation.Kind != kind || (orderId.HasValue && operation.OrderId != orderId.Value)
            || operation.ActorUserId != actorUserId)
        {
            return new StaffOrderOperationReplay(
                StaffOrderOperationReplayOutcome.OperationIdReused, operation, null);
        }

        if (!string.Equals(operation.PayloadHash, payloadHash, StringComparison.Ordinal))
        {
            return new StaffOrderOperationReplay(
                StaffOrderOperationReplayOutcome.PayloadMismatch, operation, null);
        }

        var order = await LoadOrderAsync(operation.OrderId, cancellationToken);
        return new StaffOrderOperationReplay(StaffOrderOperationReplayOutcome.Replay, operation, order);
    }

    public Task<Order?> LoadOrderAsync(Guid orderId, CancellationToken cancellationToken) =>
        _context.Orders
            .IncludeOrderLineGraph()
            .Include(order => order.Payments)
            .Include(order => order.StatusHistory)
            .Include(order => order.DeliveryAddress)
            .AsSplitQuery()
            .SingleOrDefaultAsync(order => order.Id == orderId && !order.IsDeleted, cancellationToken);
}
