using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.AccountPayments.Services;

internal static class AccountPaymentAllocationReversalMath
{
    internal static IReadOnlyList<AccountDebtSegment> Apply(
        IReadOnlyList<AccountPaymentAllocation> allocations,
        IReadOnlyList<AccountPaymentAllocationReversal> reversals,
        AccountMoney money)
    {
        var reversalsByAllocation = reversals.GroupBy(value => value.AllocationId)
            .ToDictionary(group => group.Key, group => group.OrderBy(value => value.StartOrdinal).ToArray());
        var result = new List<AccountDebtSegment>();
        foreach (var allocation in allocations)
        {
            ValidateAllocation(allocation);
            var scopes = reversalsByAllocation.GetValueOrDefault(allocation.Id) ?? [];
            AddRemainder(allocation, scopes, result, money);
        }
        if (reversals.Any(value => allocations.All(allocation => allocation.Id != value.AllocationId)))
            throw NeedsReconciliation();
        return result;
    }

    private static void ValidateAllocation(AccountPaymentAllocation allocation)
    {
        if (allocation.OrderId == Guid.Empty || allocation.MinorPerUnit <= 0 || allocation.UnitCount <= 0
            || allocation.AmountMinor != checked(allocation.MinorPerUnit * allocation.UnitCount)
            || allocation.OrderItemId is null && (allocation.StartOrdinal != 1 || allocation.UnitCount != 1)
            || allocation.OrderItemId.HasValue && allocation.StartOrdinal <= 0)
            throw NeedsReconciliation();
    }

    private static void AddRemainder(AccountPaymentAllocation allocation,
        IReadOnlyList<AccountPaymentAllocationReversal> reversals,
        List<AccountDebtSegment> result, AccountMoney money)
    {
        var start = (long)allocation.StartOrdinal;
        var end = start + allocation.UnitCount;
        var cursor = start;
        foreach (var reversal in reversals)
        {
            ValidateReversal(allocation, reversal, money);
            var reversalStart = (long)reversal.StartOrdinal;
            var reversalEnd = reversalStart + reversal.UnitCount;
            if (reversalStart < cursor || reversalStart < start || reversalEnd > end)
                throw NeedsReconciliation();
            if (cursor < reversalStart)
                AddSegment(allocation, cursor, reversalStart, result);
            cursor = reversalEnd;
        }
        if (cursor < end)
            AddSegment(allocation, cursor, end, result);
    }

    private static void ValidateReversal(AccountPaymentAllocation allocation,
        AccountPaymentAllocationReversal reversal, AccountMoney money)
    {
        if (reversal.OrderId != allocation.OrderId || reversal.OrderItemId != allocation.OrderItemId
            || reversal.MinorPerUnit != allocation.MinorPerUnit || reversal.UnitCount <= 0
            || reversal.AmountMinor != checked(reversal.MinorPerUnit * reversal.UnitCount)
            || reversal.Currency != money.Currency)
            throw NeedsReconciliation();
    }

    private static void AddSegment(AccountPaymentAllocation allocation, long start, long end,
        List<AccountDebtSegment> result)
    {
        if (start >= end)
            return;
        result.Add(new AccountDebtSegment(allocation.OrderId, allocation.OrderItemId,
            checked((int)start), checked((int)(end - start)), allocation.MinorPerUnit));
    }

    private static ConflictException NeedsReconciliation() =>
        new("A captured allocation reversal does not match its immutable tender scope.");
}
