using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Settings;

namespace RestaurantSystem.Api.Features.AccountPayments.Services;

/// <summary>Never validate a truncated refund chain; reject overflow before using any history rows.</summary>
internal static class AccountCashRefundHistoryReadLimit
{
    internal static async Task<List<T>> ReadAsync<T>(IQueryable<T> query, CancellationToken cancellationToken)
    {
        var rows = await query.Take(AccountPaymentSettings.AbsoluteCashRefundHistoryRowLimit + 1)
            .ToListAsync(cancellationToken);
        RequireWithinLimit(rows.Count);
        return rows;
    }

    internal static void RequireWithinLimit(int count)
    {
        if (count < 0 || count > AccountPaymentSettings.AbsoluteCashRefundHistoryRowLimit)
            throw new ConflictException("The cash refund history exceeds the supported reconciliation limit.");
    }
}
