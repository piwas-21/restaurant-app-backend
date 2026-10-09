using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Orders.Services;

internal static class TableBillPaymentTipReader
{
    internal static async Task<decimal> ReadAsync(
        ApplicationDbContext context, Guid? sessionId, int? tableNumber, CancellationToken cancellationToken)
    {
        var tableTipMinor = sessionId.HasValue
            ? await ReadSessionTableTipMinorAsync(context, sessionId.Value, cancellationToken)
            : await ReadLegacyTableTipMinorAsync(context, tableNumber, cancellationToken);
        var accountTipMinor = sessionId.HasValue
            ? await context.AccountPaymentAttempts.AsNoTracking()
                .Where(value => value.ServiceSessionId == sessionId
                    && value.State == AccountPaymentState.Captured)
                .Select(value => (long?)value.TipMinor).SumAsync(cancellationToken) ?? 0L
            : 0L;
        // Older ordinary till tenders can still be attached to a table bill, including after
        // legacy repair. Table/account allocation rows keep TipMinor at zero; those gratuities
        // are already represented by the operation rows above.
        var ordinaryTipMinor = await ScopedOrders(context, sessionId, tableNumber)
            .SelectMany(order => order.Payments)
            .Where(payment => payment.TableBillPaymentOperationId == null
                && (payment.Status == PaymentStatus.Completed
                    || payment.Status == PaymentStatus.PartiallyRefunded
                    || payment.Status == PaymentStatus.Refunded)
                && payment.TipMinor > payment.RefundedTipMinor)
            .Select(payment => (long?)(payment.TipMinor - payment.RefundedTipMinor))
            .SumAsync(cancellationToken) ?? 0L;
        return (decimal)tableTipMinor / 100m + (decimal)accountTipMinor / 100m
            + (decimal)ordinaryTipMinor / 100m;
    }

    internal static async Task<Dictionary<Guid, decimal>> ReadManyAsync(
        ApplicationDbContext context, IReadOnlyCollection<Guid> sessionIds, CancellationToken cancellationToken)
    {
        if (sessionIds.Count == 0)
            return [];

        var tableTips = await context.TableBillPaymentOperations.AsNoTracking()
            .Where(value => value.ServiceSessionId.HasValue && sessionIds.Contains(value.ServiceSessionId.Value))
            .GroupBy(value => value.ServiceSessionId!.Value)
            .Select(group => new { SessionId = group.Key, TipMinor = group.Sum(value => value.TipMinor) })
            .ToDictionaryAsync(value => value.SessionId, value => (decimal)value.TipMinor / 100m, cancellationToken);
        var attributedLegacyTips = await ReadLegacyTableTipMinorsBySessionAsync(context, sessionIds, cancellationToken);
        var accountTips = await context.AccountPaymentAttempts.AsNoTracking()
            .Where(value => sessionIds.Contains(value.ServiceSessionId)
                && value.State == AccountPaymentState.Captured)
            .GroupBy(value => value.ServiceSessionId)
            .Select(group => new { SessionId = group.Key, TipMinor = group.Sum(value => value.TipMinor) })
            .ToDictionaryAsync(value => value.SessionId, value => (decimal)value.TipMinor / 100m, cancellationToken);
        var ordinaryTips = await context.Orders.AsNoTracking()
            .Where(order => order.ServiceSessionId.HasValue && sessionIds.Contains(order.ServiceSessionId.Value))
            .SelectMany(order => order.Payments,
                (order, payment) => new { SessionId = order.ServiceSessionId!.Value, Payment = payment })
            .Where(value => value.Payment.TableBillPaymentOperationId == null
                && (value.Payment.Status == PaymentStatus.Completed
                    || value.Payment.Status == PaymentStatus.PartiallyRefunded
                    || value.Payment.Status == PaymentStatus.Refunded)
                && value.Payment.TipMinor > value.Payment.RefundedTipMinor)
            .GroupBy(value => value.SessionId)
            .Select(group => new
            {
                SessionId = group.Key,
                TipMinor = group.Sum(value => value.Payment.TipMinor - value.Payment.RefundedTipMinor)
            })
            .ToDictionaryAsync(value => value.SessionId, value => (decimal)value.TipMinor / 100m, cancellationToken);
        foreach (var (sessionId, tipMinor) in attributedLegacyTips)
            tableTips[sessionId] = tableTips.GetValueOrDefault(sessionId) + (decimal)tipMinor / 100m;
        foreach (var (sessionId, tip) in accountTips)
            tableTips[sessionId] = tableTips.GetValueOrDefault(sessionId) + tip;
        foreach (var (sessionId, tip) in ordinaryTips)
            tableTips[sessionId] = tableTips.GetValueOrDefault(sessionId) + tip;
        return tableTips;
    }

    private static async Task<long> ReadSessionTableTipMinorAsync(
        ApplicationDbContext context, Guid sessionId, CancellationToken cancellationToken)
    {
        var explicitTipMinor = await context.TableBillPaymentOperations.AsNoTracking()
            .Where(value => value.ServiceSessionId == sessionId)
            .Select(value => (long?)value.TipMinor).SumAsync(cancellationToken) ?? 0L;
        var attributedLegacyTips = await ReadLegacyTableTipMinorsBySessionAsync(context, [sessionId], cancellationToken);
        return explicitTipMinor + attributedLegacyTips.GetValueOrDefault(sessionId);
    }

    private static async Task<long> ReadLegacyTableTipMinorAsync(
        ApplicationDbContext context, int? tableNumber, CancellationToken cancellationToken)
    {
        var legacyOperations = await context.TableBillPaymentOperations.AsNoTracking()
            .Where(value => value.ServiceSessionId == null && value.TableNumber == tableNumber)
            .Select(value => new { value.Id, value.TipMinor })
            .ToListAsync(cancellationToken);
        var attributedSessions = await FindUniqueLegacyOperationSessionsAsync(
            context, legacyOperations.Select(value => value.Id).ToList(), cancellationToken);
        return legacyOperations.Where(value => !attributedSessions.ContainsKey(value.Id))
            .Sum(value => value.TipMinor);
    }

    private static async Task<Dictionary<Guid, long>> ReadLegacyTableTipMinorsBySessionAsync(
        ApplicationDbContext context, IReadOnlyCollection<Guid> sessionIds, CancellationToken cancellationToken)
    {
        var operationIds = await context.TableBillPaymentOperations.AsNoTracking()
            .Where(operation => operation.ServiceSessionId == null
                && context.OrderPayments.Any(payment => payment.TableBillPaymentOperationId == operation.Id
                    && payment.Order.ServiceSessionId.HasValue
                    && sessionIds.Contains(payment.Order.ServiceSessionId.Value)))
            .Select(operation => operation.Id)
            .ToListAsync(cancellationToken);
        var attributedSessions = await FindUniqueLegacyOperationSessionsAsync(context, operationIds, cancellationToken);
        var operationTips = await context.TableBillPaymentOperations.AsNoTracking()
            .Where(operation => operationIds.Contains(operation.Id))
            .Select(operation => new { operation.Id, operation.TipMinor })
            .ToListAsync(cancellationToken);
        return operationTips.Where(operation => attributedSessions.ContainsKey(operation.Id))
            .GroupBy(operation => attributedSessions[operation.Id])
            .ToDictionary(group => group.Key, group => group.Sum(operation => operation.TipMinor));
    }

    private static async Task<Dictionary<Guid, Guid>> FindUniqueLegacyOperationSessionsAsync(
        ApplicationDbContext context, List<Guid> operationIds, CancellationToken cancellationToken)
    {
        if (operationIds.Count == 0)
            return [];

        var linkedSessions = await context.OrderPayments.AsNoTracking()
            .Where(payment => payment.TableBillPaymentOperationId.HasValue
                && operationIds.Contains(payment.TableBillPaymentOperationId.Value)
                && payment.TableBillPaymentOperation!.ServiceSessionId == null)
            .Select(payment => new
            {
                OperationId = payment.TableBillPaymentOperationId!.Value,
                SessionId = payment.Order.ServiceSessionId
            })
            .Distinct()
            .ToListAsync(cancellationToken);
        return linkedSessions.GroupBy(value => value.OperationId)
            .Select(group => new
            {
                OperationId = group.Key,
                Sessions = group.Where(value => value.SessionId.HasValue)
                    .Select(value => value.SessionId!.Value).Distinct().ToArray()
            })
            .Where(value => value.Sessions.Length == 1)
            .ToDictionary(value => value.OperationId, value => value.Sessions[0]);
    }

    private static IQueryable<Order> ScopedOrders(
        ApplicationDbContext context, Guid? sessionId, int? tableNumber) => context.Orders.AsNoTracking()
            .Where(order => sessionId.HasValue
                ? order.ServiceSessionId == sessionId
                : order.ServiceSessionId == null && order.TableNumber == tableNumber);
}
