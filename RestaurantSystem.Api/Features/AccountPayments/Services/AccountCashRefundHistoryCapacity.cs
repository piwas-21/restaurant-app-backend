using System.Runtime.InteropServices;
using RestaurantSystem.Api.Settings;

namespace RestaurantSystem.Api.Features.AccountPayments.Services;

[StructLayout(LayoutKind.Auto)]
internal readonly record struct AccountCashRefundHistoryCapacitySize(
    long Attempts,
    long Allocations,
    long RefundLegs,
    long Intents,
    long AllocationReversals,
    long TillEvidence,
    long Operations)
{
    internal bool CanAdd(AccountCashRefundHistoryCapacityGrowth growth) =>
        Fits(Attempts, growth.Attempts)
        && Fits(Allocations, growth.Allocations)
        && Fits(RefundLegs, growth.RefundLegs)
        && Fits(Intents, growth.Intents)
        && Fits(AllocationReversals, growth.AllocationReversals)
        && Fits(TillEvidence, growth.TillEvidence)
        && Fits(Operations, growth.Operations);

    internal static AccountCashRefundHistoryCapacitySize ForQuote(long attempts, long allocations) =>
        new(attempts, allocations, 0, 0, 0, 0, 0);

    private static bool Fits(long current, long added)
    {
        var limit = (long)AccountPaymentSettings.AbsoluteCashRefundHistoryRowLimit;
        return current >= 0 && added >= 0 && added <= limit && current <= limit - added;
    }
}

[StructLayout(LayoutKind.Auto)]
internal readonly record struct AccountCashRefundHistoryCapacityGrowth(
    long Attempts,
    long Allocations,
    long RefundLegs,
    long Intents,
    long AllocationReversals,
    long TillEvidence,
    long Operations)
{
    private static readonly long OverLimit =
        (long)AccountPaymentSettings.AbsoluteCashRefundHistoryRowLimit + 1;

    internal static AccountCashRefundHistoryCapacityGrowth ForQuote(long allocationCount) =>
        new(1, allocationCount, 0, 0, 0, 0, 0);

    internal static AccountCashRefundHistoryCapacityGrowth ForResolution(
        IEnumerable<int> cashRefundScopeCounts)
    {
        long legCount = 0;
        long scopeCount = 0;
        foreach (var scopeCountForLeg in cashRefundScopeCounts)
        {
            legCount = AddBounded(legCount, 1);
            scopeCount = AddBounded(scopeCount, scopeCountForLeg);
        }

        return new(0, 0, legCount, legCount, scopeCount, legCount,
            legCount == 0 ? 0 : 1);
    }

    private static long AddBounded(long total, int amount)
    {
        if (amount < 0 || total < 0)
            return -1;
        if (total >= OverLimit || amount > OverLimit || total > OverLimit - amount)
            return OverLimit;
        return total + amount;
    }
}
