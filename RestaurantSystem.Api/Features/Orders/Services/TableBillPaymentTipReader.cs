using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Orders.Services;

internal static class TableBillPaymentTipReader
{
    internal static async Task<decimal> ReadAsync(
        ApplicationDbContext context, Guid? sessionId, int? tableNumber, CancellationToken cancellationToken)
    {
        var tablePayments = context.TableBillPaymentOperations.AsNoTracking().Where(value =>
            sessionId.HasValue
                ? value.ServiceSessionId == sessionId
                : value.ServiceSessionId == null && value.TableNumber == tableNumber);
        var tableTipMinor = await tablePayments.Select(value => (long?)value.TipMinor)
            .SumAsync(cancellationToken) ?? 0L;
        var accountTipMinor = sessionId.HasValue
            ? await context.AccountPaymentAttempts.AsNoTracking()
                .Where(value => value.ServiceSessionId == sessionId
                    && value.State == AccountPaymentState.Captured)
                .Select(value => (long?)value.TipMinor).SumAsync(cancellationToken) ?? 0L
            : 0L;
        return (decimal)tableTipMinor / 100m + (decimal)accountTipMinor / 100m;
    }

    internal static async Task<Dictionary<Guid, decimal>> ReadManyAsync(
        ApplicationDbContext context, IReadOnlyCollection<Guid> sessionIds, CancellationToken cancellationToken)
    {
        var tableTips = await context.TableBillPaymentOperations.AsNoTracking()
            .Where(value => value.ServiceSessionId.HasValue && sessionIds.Contains(value.ServiceSessionId.Value))
            .GroupBy(value => value.ServiceSessionId!.Value)
            .Select(group => new { SessionId = group.Key, TipMinor = group.Sum(value => value.TipMinor) })
            .ToDictionaryAsync(value => value.SessionId, value => (decimal)value.TipMinor / 100m, cancellationToken);
        var accountTips = await context.AccountPaymentAttempts.AsNoTracking()
            .Where(value => sessionIds.Contains(value.ServiceSessionId)
                && value.State == AccountPaymentState.Captured)
            .GroupBy(value => value.ServiceSessionId)
            .Select(group => new { SessionId = group.Key, TipMinor = group.Sum(value => value.TipMinor) })
            .ToDictionaryAsync(value => value.SessionId, value => (decimal)value.TipMinor / 100m, cancellationToken);
        foreach (var (sessionId, tip) in accountTips)
            tableTips[sessionId] = tableTips.GetValueOrDefault(sessionId) + tip;
        return tableTips;
    }
}
