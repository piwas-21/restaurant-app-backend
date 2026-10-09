using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Api.Features.TableServiceSessions.Services;

namespace RestaurantSystem.Api.Features.TableServiceSessions.Commands.RecoverTableOccupancyCommand;

public sealed partial class RecoverTableOccupancyCommandHandler
{
    private static TableOccupancyRecoveryDisposition CreateDisposition(
        TableOccupancyRecoverySnapshot snapshot, Order order,
        TableOccupancyRecoveryDispositionKind kind, Guid operationId, DateTime now, string audit) => new()
        {
            Id = Guid.NewGuid(),
            OperationId = operationId,
            TableId = snapshot.Table.Id,
            OrderId = order.Id,
            ServiceSessionId = order.ServiceSessionId,
            OrderNumber = order.OrderNumber,
            Kind = kind,
            WasLegacyUnassigned = snapshot.LegacyOrderIds.Contains(order.Id),
            OriginalStatus = order.Status,
            OriginalPaymentStatus = order.PaymentStatus,
            OriginalTotal = order.Total,
            OriginalBillingCreditAmount = order.BillingCreditAmount,
            OriginalTotalPaid = order.TotalPaid,
            OriginalRemainingAmount = order.RemainingAmount,
            WasKitchenReleased = order.IsKitchenReleased || order.KitchenReleasedAt.HasValue,
            HadRoutingHistory = order.RoutingStates.Count > 0,
            RecordedAt = now,
            CreatedAt = now,
            CreatedBy = audit
        };

    private void CancelUnsentOrder(Order order, Guid operationId, DateTime now, string audit)
    {
        var fromStatus = order.Status;
        var reason = $"Cancelled during table occupancy recovery {operationId}.";
        order.Status = OrderStatus.Cancelled;
        order.CancellationReason = reason;
        order.UpdatedAt = now;
        order.UpdatedBy = audit;
        context.OrderStatusHistories.Add(new OrderStatusHistory
        {
            Id = Guid.NewGuid(),
            OrderId = order.Id,
            FromStatus = fromStatus,
            ToStatus = OrderStatus.Cancelled,
            Notes = reason,
            ChangedAt = now,
            ChangedBy = audit,
            CreatedAt = now,
            CreatedBy = audit
        });
    }
}
