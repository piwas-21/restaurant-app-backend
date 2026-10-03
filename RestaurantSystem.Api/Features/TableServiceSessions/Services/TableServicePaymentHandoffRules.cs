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
        ReadOutstandingCoreAsync(context, sessionId, cancellationToken);

    private static async Task<decimal> ReadOutstandingCoreAsync(
        ApplicationDbContext context, Guid sessionId, CancellationToken cancellationToken)
    {
        var rows = await context.Orders
            .Where(order => !order.IsDeleted && order.ServiceSessionId == sessionId)
            .Where(OrderSettlementEligibility.CanCollectQuery())
            .Select(order => new
            {
                order.Total,
                order.BillingCreditAmount,
                order.TotalPaid
            })
            .ToListAsync(cancellationToken);
        return rows.Sum(order => TableServiceSessionCloseRules.EffectiveOutstanding(
            order.Total, order.BillingCreditAmount, order.TotalPaid));
    }

    public static async Task<bool> HasBlockingLegacyAsync(
        ApplicationDbContext context, TableServiceSession session,
        decimal paymentTolerance, CancellationToken cancellationToken)
    {
        var rows = await TableServiceSessionCloseRules.ForUnassignedSession(
                context.Orders.Where(order => !order.IsDeleted && order.Type == OrderType.DineIn
                    && order.ServiceSessionId == null), session.TableId, session.TableNumber)
            .Select(order => new
            {
                order.Status,
                order.Total,
                order.BillingCreditAmount,
                order.TotalPaid,
                order.PaymentStatus
            })
            .ToListAsync(cancellationToken);
        return rows.Any(row => TableServiceSessionCloseRules.IsBlockingLegacyOrder(
            TableServiceSessionCloseRules.FromCharge(
                row.Status, row.Total, row.BillingCreditAmount, row.TotalPaid,
                row.PaymentStatus == PaymentStatus.Refunded), paymentTolerance));
    }
}
