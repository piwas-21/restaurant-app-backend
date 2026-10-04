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
            .SelectMany(value => value.Allocations).ToArray();
        foreach (var group in allocations.GroupBy(value => value.OrderPaymentId))
        {
            var payment = group.First().OrderPayment;
            if (payment is null || !payment.Status.IsCaptured()
                || group.Any(value => value.OrderId != payment.OrderId
                    || value.AmountMinor != checked(value.MinorPerUnit * value.UnitCount))
                || group.Sum(value => value.AmountMinor) != money.ToMinor(payment.Amount))
                throw new ConflictException("A captured account allocation has no matching tender evidence.");
        }
    }
}
