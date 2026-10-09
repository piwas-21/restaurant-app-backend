using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.AccountPayments.Services;

/// <summary>Records exact manual money and allocation links atomically; no gateway call or tolerance.</summary>
public sealed class AccountPaymentCaptureWriter(
    ApplicationDbContext context, ICurrentUserService currentUser, TimeProvider timeProvider)
    : IAccountPaymentCaptureWriter
{
    public async Task RecordManualAsync(AccountPaymentAttempt attempt, CancellationToken cancellationToken)
    {
        RequireManualTransaction(attempt);
        var account = await LoadOpenAccountAsync(attempt.ServiceSessionId, cancellationToken);
        var scopes = CreateAndValidateScopes(attempt, account.Money.Currency);
        AccountDebtMath.Subtract(account.Debt.Outstanding, scopes);
        var orders = await LoadScopedOrdersAsync(attempt, scopes, cancellationToken);

        var now = PostgresTimestampPrecision.TruncateToMicrosecond(timeProvider.GetUtcNow().UtcDateTime);
        var audit = currentUser.GetAuditIdentifier();
        foreach (var order in orders)
            PostOrderPayment(order, attempt, account.Money, now, audit);
        MarkCaptured(attempt, now, audit);
    }

    public async Task RecordVerifiedProviderAsync(AccountPaymentAttempt attempt, AccountCheckoutJournal journal,
        CancellationToken cancellationToken)
    {
        RequireVerifiedProvider(attempt, journal);
        var account = await new AccountDebtSnapshotReader(context)
            .ReadForVerifiedCaptureAsync(attempt.ServiceSessionId, attempt.Id, cancellationToken);
        var scopes = CreateAndValidateScopes(attempt, account.Money.Currency);
        AccountDebtMath.Subtract(account.Debt.Outstanding, scopes);
        var orders = await LoadScopedOrdersAsync(attempt, scopes, cancellationToken);
        var now = PostgresTimestampPrecision.TruncateToMicrosecond(timeProvider.GetUtcNow().UtcDateTime);
        foreach (var order in orders)
            PostOrderPayment(order, attempt, account.Money, now, attempt.CreatedBy, journal.ProviderChargeId);
        MarkCaptured(attempt, now, attempt.CreatedBy);
    }

    private void RequireVerifiedProvider(AccountPaymentAttempt attempt, AccountCheckoutJournal journal)
    {
        if (context.Database.CurrentTransaction is null || journal.AttemptId != attempt.Id
            || attempt.PaymentMethod != PaymentMethod.OnlinePayment
            || attempt.State != AccountPaymentState.Processing || attempt.AmountMinor != journal.AmountMinor
            || attempt.Currency != journal.Currency || journal.ProviderCapturedMinor != attempt.AmountMinor
            || journal.ProviderRefundedMinor != 0 || journal.ProviderSessionId != attempt.ProviderSessionId
            || journal.ProviderAccountId != attempt.ProviderAccountId
            || journal.ProviderChargeId != attempt.ProviderChargeId
            || journal.ProviderChargeId?.StartsWith("ch_", StringComparison.Ordinal) != true)
            throw new ConflictException("Online collection requires exact canonical provider capture evidence.");
    }

    private void RequireManualTransaction(AccountPaymentAttempt attempt)
    {
        if (context.Database.CurrentTransaction is null || attempt.State != AccountPaymentState.Reserved
            || attempt.PaymentMethod is not (PaymentMethod.Cash or PaymentMethod.CreditCard))
            throw new ConflictException("Manual collection requires a reserved cash or card contribution and a locked transaction.");
    }

    private async Task<AccountPaymentAccountSnapshot> LoadOpenAccountAsync(
        Guid sessionId, CancellationToken cancellationToken)
    {
        var account = await new AccountDebtSnapshotReader(context).ReadAsync(sessionId, cancellationToken);
        if (account.Session.Status != TableServiceSessionStatus.Open)
            throw new ConflictException("A closed visit cannot accept manual collection.");
        return account;
    }

    private static AccountDebtSegment[] CreateAndValidateScopes(
        AccountPaymentAttempt attempt, string currency)
    {
        var scopes = attempt.Allocations.Select(value => new AccountDebtSegment(
            value.OrderId, value.OrderItemId, value.StartOrdinal, value.UnitCount, value.MinorPerUnit)).ToArray();
        if (attempt.Currency != currency || attempt.AmountMinor <= 0
            || scopes.Length == 0 || AccountDebtMath.Total(scopes) != attempt.AmountMinor
            || attempt.Allocations.Any(value => value.OrderPaymentId.HasValue
                || value.AmountMinor != checked(value.MinorPerUnit * value.UnitCount)))
            throw new ConflictException("The reviewed contribution requires reconciliation before collection.");
        return scopes;
    }

    private async Task<List<Order>> LoadScopedOrdersAsync(
        AccountPaymentAttempt attempt,
        IReadOnlyList<AccountDebtSegment> scopes,
        CancellationToken cancellationToken)
    {
        var orderIds = scopes.Select(value => value.OrderId).Distinct().ToArray();
        var orders = await context.Orders.Where(value => orderIds.Contains(value.Id))
            .Include(value => value.Payments).ToListAsync(cancellationToken);
        if (orders.Count != orderIds.Length || orders.Any(value => value.ServiceSessionId != attempt.ServiceSessionId))
            throw new ConflictException("The reviewed payment scope no longer belongs to this visit.");
        return orders;
    }

    private void PostOrderPayment(
        Order order, AccountPaymentAttempt attempt, AccountMoney money, DateTime now, string audit,
        string? providerChargeId = null)
    {
        var allocations = attempt.Allocations.Where(value => value.OrderId == order.Id).ToArray();
        var amount = money.ToMajor(allocations.Sum(value => value.AmountMinor));
        RemovePendingPlaceholders(order);
        var payment = CreatePayment(order.Id, amount, attempt.PaymentMethod, money, now, audit);
        if (providerChargeId is not null)
        {
            payment.TransactionId = providerChargeId;
            payment.PaymentGateway = "Stripe";
        }
        order.Payments.Add(payment);
        context.OrderPayments.Add(payment);
        LinkAllocations(allocations, payment);
        RecalculateOrderPaymentState(order, money, now, audit);
    }

    private void RemovePendingPlaceholders(Order order)
    {
        foreach (var placeholder in order.Payments.Where(value => value.Status == PaymentStatus.Pending).ToArray())
        {
            order.Payments.Remove(placeholder);
            context.OrderPayments.Remove(placeholder);
        }
    }

    private static OrderPayment CreatePayment(
        Guid orderId,
        decimal amount,
        PaymentMethod paymentMethod,
        AccountMoney money,
        DateTime now,
        string audit) => new()
        {
            Id = Guid.NewGuid(),
            OrderId = orderId,
            Amount = amount,
            PaymentMethod = paymentMethod,
            Status = PaymentStatus.Completed,
            Currency = money.Currency,
            PaymentDate = now,
            CreatedAt = now,
            CreatedBy = audit
        };

    private static void LinkAllocations(IEnumerable<AccountPaymentAllocation> allocations, OrderPayment payment)
    {
        foreach (var allocation in allocations)
        {
            allocation.OrderPaymentId = payment.Id;
            allocation.OrderPayment = payment;
        }
    }

    private static void RecalculateOrderPaymentState(Order order, AccountMoney money, DateTime now, string audit)
    {
        var paidMinor = order.Payments.Where(value => value.Status.IsCaptured())
            .DistinctBy(value => value.Id).Sum(value => money.ToMinor(value.Amount));
        var totalMinor = money.ToMinor(order.PayableTotal);
        if (paidMinor > totalMinor)
            throw new ConflictException("Collection exceeds the frozen order charge.");
        order.TotalPaid = money.ToMajor(paidMinor);
        order.RemainingAmount = money.ToMajor(totalMinor - paidMinor);
        order.PaymentStatus = paidMinor == totalMinor ? PaymentStatus.Completed : PaymentStatus.PartiallyPaid;
        order.UpdatedAt = now;
        order.UpdatedBy = audit;
    }

    private static void MarkCaptured(AccountPaymentAttempt attempt, DateTime now, string audit)
    {
        attempt.State = AccountPaymentState.Captured;
        attempt.Version++;
        attempt.CompletedAt = now;
        attempt.UpdatedAt = now;
        attempt.UpdatedBy = audit;
    }
}
