using RestaurantSystem.Api.Common.Exceptions;

namespace RestaurantSystem.Api.Features.AccountPayments.Services;

/// <summary>Allocates custom guest shares from disjoint offsets in the immutable reviewed scope.</summary>
internal static class AccountCustomShareScopeMath
{
    internal static IReadOnlyList<AccountDebtSegment> ForShare(
        IReadOnlyList<AccountDebtSegment> reviewedScope, IReadOnlyList<long> amounts, int oneBasedOrdinal)
    {
        long total;
        try
        {
            total = amounts.Aggregate(0L, (sum, amount) => checked(sum + amount));
        }
        catch (OverflowException exception)
        {
            throw new ConflictException("The custom guest split plan requires reconciliation.", exception);
        }

        var reviewedTotal = AccountDebtMath.Total(reviewedScope);
        if (amounts.Count == 0 || amounts.Any(value => value <= 0)
            || oneBasedOrdinal < 1 || oneBasedOrdinal > amounts.Count || total != reviewedTotal)
            throw new ConflictException("The custom guest split plan requires reconciliation.");

        var prefix = amounts.Take(oneBasedOrdinal - 1)
            .Aggregate(0L, (sum, amount) => checked(sum + amount));
        var remainder = prefix == 0
            ? reviewedScope
            : AccountDebtMath.Subtract(reviewedScope, AccountDebtMath.Amount(reviewedScope, prefix));
        return AccountDebtMath.Amount(remainder, amounts[oneBasedOrdinal - 1]);
    }
}
