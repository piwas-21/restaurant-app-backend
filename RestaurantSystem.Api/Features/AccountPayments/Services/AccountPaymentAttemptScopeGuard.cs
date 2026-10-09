using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.AccountPayments.Services;

/// <summary>Complete frozen attempt and tender totals must reconcile before any payment or refund planning.</summary>
internal static class AccountPaymentAttemptScopeGuard
{
    internal static void RequireReconciled(IReadOnlyList<AccountPaymentAttempt> attempts, AccountMoney money)
    {
        var active = attempts.Where(value => value.State == AccountPaymentState.Captured
            || value.State.HoldsReservation()).ToArray();
        if (active.Any(attempt => attempt.Currency != money.Currency
                || attempt.Allocations.Sum(allocation => allocation.AmountMinor) != attempt.AmountMinor))
            throw new ConflictException("The account's captured or reserved totals require reconciliation.");
        var allocations = active.Where(value => value.State == AccountPaymentState.Captured)
            .SelectMany(attempt => attempt.Allocations.Select(allocation => (Attempt: attempt, Allocation: allocation)))
            .ToArray();
        foreach (var group in allocations.GroupBy(value => value.Allocation.OrderPaymentId))
        {
            var payment = group.First().Allocation.OrderPayment;
            if (payment is null || !payment.Status.IsCaptured()
                || group.Select(value => value.Attempt.Id).Distinct().Count() != 1
                || group.Any(value => value.Allocation.OrderId != payment.OrderId
                    || value.Attempt.PaymentMethod != payment.PaymentMethod
                    || value.Allocation.AmountMinor != checked(
                        value.Allocation.MinorPerUnit * value.Allocation.UnitCount))
                || group.Sum(value => value.Allocation.AmountMinor) != money.ToMinor(payment.Amount))
                throw new ConflictException("A captured account allocation has no matching tender evidence.");
        }
    }
}
