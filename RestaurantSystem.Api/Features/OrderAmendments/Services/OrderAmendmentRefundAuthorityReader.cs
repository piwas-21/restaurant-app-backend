using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.AccountPayments.Services;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.OrderAmendments.Services;

internal sealed record OrderAmendmentRefundAuthoritySnapshot(
    AccountMoney Money,
    IReadOnlyDictionary<Guid, long> AuthorizedRefundMinorByPayment);

/// <summary>Reads the immutable local evidence that authorizes previously posted amendment refunds.</summary>
internal static class OrderAmendmentRefundAuthorityReader
{
    internal static async Task<OrderAmendmentRefundAuthoritySnapshot> ReadAsync(
        ApplicationDbContext context,
        Order source,
        IOrderDisplayCurrencyResolver currencyResolver,
        CancellationToken cancellationToken)
    {
        var currency = source.ServiceSession?.Currency ?? currencyResolver.Resolve(source);
        AccountMoney money;
        try
        {
            money = new AccountMoney(currency);
        }
        catch (BadRequestException)
        {
            throw ReconciliationRequired();
        }

        var amendments = await context.OrderAmendments.AsNoTracking()
            .Where(value => value.SourceOrderId == source.Id
                && value.State == RestaurantSystem.Domain.Common.Enums.OrderAmendmentState.Committed)
            .ToListAsync(cancellationToken);
        var attempts = source.ServiceSessionId is Guid accountId
            ? await context.AccountPaymentAttempts.AsNoTracking()
                .Where(value => value.ServiceSessionId == accountId)
                .Where(value => value.Allocations.Any(allocation => allocation.OrderId == source.Id))
                .Include(value => value.Allocations.Where(allocation => allocation.OrderId == source.Id))
                .ThenInclude(value => value.OrderPayment)
                .AsSplitQuery().ToListAsync(cancellationToken)
            : [];

        var refunds = await AccountAmendmentRefundIntegrity.ReadAsync(
            context, [source], amendments, attempts, money, cancellationToken);
        return new OrderAmendmentRefundAuthoritySnapshot(
            money, refunds.AuthorizedRefundMinorByPayment);
    }

    internal static ConflictException ReconciliationRequired() => new(
        "The order has refund activity without matching resolved amendment evidence.");
}
