using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Domain.Common;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Orders.Services;

internal static class OrderNativeAcceptedCurrency
{
    internal static Task<string?> ReadTenantCurrencyAsync(
        ApplicationDbContext context, CancellationToken cancellationToken) =>
        context.RestaurantInfo.AsNoTracking()
            .Select(restaurant => restaurant.Currency)
            .FirstOrDefaultAsync(cancellationToken);

    internal static async Task<string?> ResolveForAcceptanceAsync(
        ApplicationDbContext context,
        string? frozenVisitCurrency,
        CancellationToken cancellationToken)
    {
        var visitCurrency = CurrencyCode.Normalize(frozenVisitCurrency);
        var catalogueCurrency = CurrencyCode.Normalize(
            await ReadTenantCurrencyAsync(context, cancellationToken));
        if (visitCurrency is not null && catalogueCurrency is not null
            && !string.Equals(visitCurrency, catalogueCurrency, StringComparison.Ordinal))
        {
            throw new ConflictException(
                "The table visit currency changed from the live catalogue currency. Review the visit before adding items.");
        }

        return visitCurrency ?? catalogueCurrency;
    }

    internal static async Task<string?> ReadOrderCurrencyEvidenceAsync(
        ApplicationDbContext context,
        Order source,
        CancellationToken cancellationToken)
    {
        var snapshot = await context.OrderBillingSnapshots.AsNoTracking()
            .SingleOrDefaultAsync(value => value.OrderId == source.Id, cancellationToken);
        var snapshotCurrency = CurrencyCode.Normalize(snapshot?.Currency);
        if (snapshot is not null && snapshotCurrency is null)
        {
            throw new ConflictException("The source order has an invalid accepted-currency snapshot.");
        }

        var sessionCurrency = source.ServiceSessionId.HasValue
            ? await context.TableServiceSessions.AsNoTracking()
                .Where(value => value.Id == source.ServiceSessionId.Value)
                .Select(value => value.Currency)
                .SingleOrDefaultAsync(cancellationToken)
            : null;
        var providerCurrency = await context.ExternalOrderReferences.AsNoTracking()
            .Where(value => value.OrderId == source.Id)
            .Select(value => value.Currency)
            .SingleOrDefaultAsync(cancellationToken);
        var tenderCurrencies = await context.OrderPayments.AsNoTracking()
            .Where(value => value.OrderId == source.Id && value.Currency != null)
            .Select(value => value.Currency)
            .ToListAsync(cancellationToken);
        // Every persisted attempt remains accepted-currency evidence; a newer session cannot erase
        // the currency of an earlier payable or completed attempt.
        var checkoutCurrencies = await context.OrderCheckoutSessions.AsNoTracking()
            .Where(value => value.OrderId == source.Id)
            .Select(value => value.Currency)
            .ToListAsync(cancellationToken);

        return ResolveConsistentEvidence(
            [
                snapshotCurrency,
                sessionCurrency,
                providerCurrency,
                .. tenderCurrencies,
                .. checkoutCurrencies
            ]);
    }

    internal static string? ResolveConsistentEvidence(IEnumerable<string?> currencyEvidence)
    {
        var currencies = currencyEvidence
            .Select(CurrencyCode.Normalize)
            .Where(value => value is not null)
            .Select(value => value!)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (currencies.Length > 1)
        {
            throw new ConflictException(
                "The order has conflicting accepted-currency evidence. Review its payment history before retrying.");
        }

        return currencies.SingleOrDefault();
    }
}
