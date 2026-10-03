using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.AccountPayments.Services;

/// <summary>Requires durable Stripe clearance before a legacy online tender can reduce visit debt.</summary>
internal static class AccountCheckoutEvidenceGuard
{
    internal static async Task ValidateSessionAsync(
        ApplicationDbContext context, Guid serviceSessionId, CancellationToken cancellationToken)
    {
        var session = await context.TableServiceSessions.AsNoTracking()
            .SingleOrDefaultAsync(value => value.Id == serviceSessionId, cancellationToken)
            ?? throw new NotFoundException("Table account was not found.");
        var orders = await context.Orders.AsNoTracking()
            .Where(value => value.ServiceSessionId == serviceSessionId && !value.IsDeleted)
            .Include(value => value.Payments).ToListAsync(cancellationToken);
        var orderIds = orders.Select(order => order.Id).ToArray();
        var checkouts = await context.OrderCheckoutSessions.AsNoTracking()
            .Where(value => orderIds.Contains(value.OrderId))
            .ToListAsync(cancellationToken);

        var hasOnlineEvidence = checkouts.Count > 0 || orders.SelectMany(order => order.Payments).Any(payment =>
            payment.PaymentMethod == PaymentMethod.OnlinePayment
            && (payment.Status == PaymentStatus.Processing || payment.Status.IsCaptured()));
        if (!hasOnlineEvidence)
            return;

        var attempts = await context.AccountPaymentAttempts.AsNoTracking()
            .Where(value => value.ServiceSessionId == serviceSessionId)
            .Include(value => value.Allocations).ThenInclude(value => value.OrderPayment)
            .AsSplitQuery().ToListAsync(cancellationToken);

        Validate(orders, checkouts, attempts, new AccountMoney(session.Currency));
    }

    internal static void Validate(
        IReadOnlyList<Order> orders,
        IReadOnlyList<OrderCheckoutSession> checkouts,
        IReadOnlyList<AccountPaymentAttempt> attempts,
        AccountMoney money)
    {
        var ordersById = orders.ToDictionary(value => value.Id);
        var payments = orders.SelectMany(order => order.Payments).ToDictionary(value => value.Id);
        var clearedPaymentIds = new HashSet<Guid>();
        var clearedIntentIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var checkout in checkouts)
        {
            if (checkout.ReconciledAt.HasValue && checkout.LastError is not null)
                throw NeedsReconciliation();
            if (!Enum.IsDefined(checkout.Status))
                throw NeedsReconciliation();
            if (checkout.Status == CheckoutSessionStatus.Created)
                throw new ConflictException("A Stripe checkout is still active for this table account.");
            if (checkout.Status != CheckoutSessionStatus.Completed)
            {
                if (checkout.OrderPaymentId is Guid terminalPaymentId
                    && payments.TryGetValue(terminalPaymentId, out var terminalPayment)
                    && terminalPayment.Status.IsCaptured())
                    throw NeedsReconciliation();
                continue;
            }

            if (!checkout.ReconciledAt.HasValue || checkout.LastError is not null
                || checkout.OrderPaymentId is not Guid paymentId
                || !ordersById.TryGetValue(checkout.OrderId, out var order)
                || !payments.TryGetValue(paymentId, out var payment)
                || !IsCanonicalIdentity(checkout)
                || payment.OrderId != order.Id
                || payment.PaymentMethod != PaymentMethod.OnlinePayment
                || !payment.Status.IsCaptured()
                || !string.Equals(payment.PaymentGateway, "Stripe", StringComparison.OrdinalIgnoreCase)
                || !string.Equals(payment.TransactionId, checkout.PaymentIntentId, StringComparison.Ordinal)
                || !string.Equals(checkout.Currency, money.Currency, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(payment.Currency, checkout.Currency, StringComparison.OrdinalIgnoreCase)
                || checkout.AmountMinor <= 0
                || checkout.AmountReceivedMinor != checkout.AmountMinor
                || ToMinorOrReconcile(money, payment.Amount) != checkout.AmountMinor
                || !clearedPaymentIds.Add(payment.Id)
                || !clearedIntentIds.Add(checkout.PaymentIntentId!))
                throw NeedsReconciliation();
        }

        foreach (var payment in payments.Values.Where(value => value.PaymentMethod == PaymentMethod.OnlinePayment
                     && value.Status.IsCaptured()))
        {
            if (clearedPaymentIds.Contains(payment.Id))
                continue;
            if (!HasExactCapturedAccountAllocation(payment, attempts, money))
                throw NeedsReconciliation();
        }
    }

    private static bool IsCanonicalIdentity(OrderCheckoutSession checkout) =>
        !string.IsNullOrWhiteSpace(checkout.SessionId)
        && checkout.SessionId.StartsWith("cs_", StringComparison.Ordinal)
        && checkout.PaymentIntentId?.StartsWith("pi_", StringComparison.Ordinal) == true
        && !string.IsNullOrWhiteSpace(checkout.ConnectedAccountId)
        && checkout.ConnectedAccountId.StartsWith("acct_", StringComparison.Ordinal);

    private static bool HasExactCapturedAccountAllocation(
        OrderPayment payment, IReadOnlyList<AccountPaymentAttempt> attempts, AccountMoney money)
    {
        var matching = attempts.Where(attempt => attempt.State == AccountPaymentState.Captured
                && attempt.PaymentMethod == payment.PaymentMethod
                && attempt.Currency == money.Currency)
            .SelectMany(attempt => attempt.Allocations.Where(allocation => allocation.OrderPaymentId == payment.Id)
                .Select(allocation => (Attempt: attempt, Allocation: allocation)))
            .ToArray();
        if (matching.Length == 0 || matching.Select(value => value.Attempt.Id).Distinct().Count() != 1)
            return false;
        try
        {
            var allocationTotal = matching.Sum(value => value.Allocation.AmountMinor);
            var attemptAllocationTotal = matching[0].Attempt.Allocations.Sum(value => value.AmountMinor);
            return matching.All(value => value.Allocation.OrderId == payment.OrderId
                    && value.Allocation.AmountMinor == checked(value.Allocation.MinorPerUnit * value.Allocation.UnitCount))
                && matching[0].Attempt.AmountMinor == attemptAllocationTotal
                && HasProviderCaptureIdentity(matching[0].Attempt, payment)
                && allocationTotal == ToMinorOrReconcile(money, payment.Amount);
        }
        catch (OverflowException)
        {
            return false;
        }
    }

    private static bool HasProviderCaptureIdentity(AccountPaymentAttempt attempt, OrderPayment payment) =>
        string.Equals(payment.PaymentGateway, "Stripe", StringComparison.OrdinalIgnoreCase)
        && !string.IsNullOrWhiteSpace(attempt.ProviderAccountId)
        && attempt.ProviderAccountId.StartsWith("acct_", StringComparison.Ordinal)
        && !string.IsNullOrWhiteSpace(attempt.ProviderChargeId)
        && (attempt.ProviderChargeId.StartsWith("pi_", StringComparison.Ordinal)
            || attempt.ProviderChargeId.StartsWith("ch_", StringComparison.Ordinal))
        && string.Equals(attempt.ProviderChargeId, payment.TransactionId, StringComparison.Ordinal);

    private static long ToMinorOrReconcile(AccountMoney money, decimal amount)
    {
        try
        {
            return money.ToMinor(amount);
        }
        catch (BadRequestException exception)
        {
            throw new ConflictException("A Stripe tender amount requires reconciliation.", exception);
        }
    }

    private static ConflictException NeedsReconciliation() =>
        new("A Stripe checkout or online tender lacks verified capture evidence. Reconciliation is required.");
}
