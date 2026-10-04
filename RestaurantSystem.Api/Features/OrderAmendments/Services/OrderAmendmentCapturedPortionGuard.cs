using System.Numerics;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.AccountPayments.Services;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.OrderAmendments.Services;

/// <summary>Different payers may contribute to one unit, but their frozen portions cannot exceed its charge.</summary>
internal static class OrderAmendmentCapturedPortionGuard
{
    internal static void RequireConserved(
        Order source, IReadOnlyList<AccountPaymentAllocation> allocations, AccountMoney money)
    {
        if (allocations.Count == 0)
            return;
        if (allocations.Select(value => value.Id).Distinct().Count() != allocations.Count
            || allocations.Any(value => value.Id == Guid.Empty || value.AttemptId == Guid.Empty
                || value.OrderId != source.Id))
            throw ReconciliationRequired();

        var captured = new List<AccountDebtSegment>(allocations.Count);
        foreach (var allocation in allocations)
        {
            if (allocation.OrderPaymentId is null || allocation.OrderPaymentId == Guid.Empty
                || allocation.OrderItemId == Guid.Empty || allocation.StartOrdinal < 1
                || allocation.UnitCount <= 0 || allocation.MinorPerUnit <= 0 || allocation.AmountMinor <= 0
                || (long)allocation.StartOrdinal + allocation.UnitCount > (long)int.MaxValue + 1
                || new BigInteger(allocation.MinorPerUnit) * allocation.UnitCount != allocation.AmountMinor
                || allocation.OrderItemId is null && (allocation.StartOrdinal != 1 || allocation.UnitCount != 1))
                throw ReconciliationRequired();
            captured.Add(new AccountDebtSegment(allocation.OrderId, allocation.OrderItemId,
                allocation.StartOrdinal, allocation.UnitCount, allocation.MinorPerUnit));
        }

        var frozen = FrozenOrderChargeMath.Read(source, money);
        var due = frozen.FoodLines.SelectMany(line => AccountDebtMath.CreateLine(
            source.Id, line.Item.Id, line.Item.Quantity, line.AmountMinor, 0))
            .Concat(AccountDebtMath.CreateUnitemized(source.Id,
                checked(frozen.TipMinor + frozen.FeeMinor), 0)).ToArray();
        // This sweep sums overlapping monetary portions per ordinal without expanding quantities.
        _ = AccountDebtMath.Subtract(due, captured);
    }

    private static ConflictException ReconciliationRequired() =>
        new("Captured payment portions do not match distinct immutable allocations of the frozen charge.");
}
