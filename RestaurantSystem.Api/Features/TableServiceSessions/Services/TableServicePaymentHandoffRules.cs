using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.TableServiceSessions.Services;

public static class TableServicePaymentHandoffRules
{
    public static Task<bool> HasPendingAsync(
        ApplicationDbContext context, Guid sessionId, CancellationToken cancellationToken) =>
        context.TableServicePaymentHandoffs.AnyAsync(
            handoff => handoff.ServiceSessionId == sessionId
                && handoff.Status == TableServicePaymentHandoffStatus.Requested,
            cancellationToken);

    public static Task<decimal> ReadOutstandingAsync(
        ApplicationDbContext context, Guid sessionId, CancellationToken cancellationToken) =>
        context.Orders
            .Where(order => !order.IsDeleted && order.ServiceSessionId == sessionId)
            .Where(OrderSettlementEligibility.CanCollectQuery())
            .Select(order => order.RemainingAmount)
            .SumAsync(cancellationToken);

    public static async Task<bool> HasBlockingLegacyAsync(
        ApplicationDbContext context, TableServiceSession session,
        decimal paymentTolerance, CancellationToken cancellationToken)
    {
        var rows = await TableServiceSessionCloseRules.ForUnassignedSession(
                context.Orders.Where(order => !order.IsDeleted && order.Type == OrderType.DineIn
                    && order.ServiceSessionId == null), session.TableId, session.TableNumber)
            .Select(order => new { order.Status, order.RemainingAmount })
            .ToListAsync(cancellationToken);
        return rows.Any(row => TableServiceSessionCloseRules.IsBlockingLegacyOrder(
            new TableServiceSessionOrderState(row.Status, row.RemainingAmount), paymentTolerance));
    }
}
