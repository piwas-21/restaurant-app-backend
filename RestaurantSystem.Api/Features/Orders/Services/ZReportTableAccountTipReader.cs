using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Domain.Common;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Orders.Services;

internal static class ZReportTableAccountTipReader
{
    internal static async Task<IReadOnlyList<ZReportTableAccountTipDto>> ReadAsync(
        ApplicationDbContext context, DateTime startUtc, DateTime endUtc, CancellationToken cancellationToken)
    {
        var tableTips = await context.TableBillPaymentOperations.AsNoTracking()
            .Where(value => value.TipMinor > 0
                && value.CreatedAt >= startUtc && value.CreatedAt < endUtc)
            .GroupBy(value => new { value.Currency, value.PaymentMethod })
            .Select(group => new TipTotal(group.Key.Currency, group.Key.PaymentMethod,
                group.Sum(value => value.TipMinor)))
            .ToListAsync(cancellationToken);

        var accountTips = await context.AccountPaymentAttempts.AsNoTracking()
            .Where(value => value.State == AccountPaymentState.Captured && value.TipMinor > 0
                && value.CompletedAt.HasValue && value.CompletedAt.Value >= startUtc
                && value.CompletedAt.Value < endUtc)
            .GroupBy(value => new { value.Currency, value.PaymentMethod })
            .Select(group => new TipTotal(group.Key.Currency, group.Key.PaymentMethod,
                group.Sum(value => value.TipMinor)))
            .ToListAsync(cancellationToken);

        var totals = new Dictionary<(string Currency, PaymentMethod Method), long>();
        foreach (var row in tableTips.Concat(accountTips))
        {
            var currency = CurrencyKey(row.Currency);
            var key = (currency, row.PaymentMethod);
            totals[key] = checked(totals.GetValueOrDefault(key) + row.TipMinor);
        }

        return totals.Select(pair => new ZReportTableAccountTipDto(
                NullableCurrency(pair.Key.Currency), pair.Key.Method, pair.Value))
            .OrderBy(value => value.Currency, StringComparer.Ordinal)
            .ThenBy(value => value.PaymentMethod)
            .ToArray();
    }

    private static string CurrencyKey(string? value) =>
        CurrencyCode.IsValid(value) ? CurrencyCode.Normalize(value) ?? string.Empty : string.Empty;

    private static string? NullableCurrency(string currency) => currency.Length == 0 ? null : currency;

    private sealed record TipTotal(string? Currency, PaymentMethod PaymentMethod, long TipMinor);
}
