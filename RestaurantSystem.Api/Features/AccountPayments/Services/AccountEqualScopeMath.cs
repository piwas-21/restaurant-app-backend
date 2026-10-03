using RestaurantSystem.Api.Common.Exceptions;

namespace RestaurantSystem.Api.Features.AccountPayments.Services;

/// <summary>Each share retains its original reviewed scope, regardless of payment order.</summary>
internal static class AccountEqualScopeMath
{
    internal static IReadOnlyList<AccountDebtSegment> ForShare(
        IReadOnlyList<AccountDebtSegment> reviewedScope, int shareCount, int oneBasedOrdinal)
    {
        var total = AccountDebtMath.Total(reviewedScope);
        var distribution = AccountShareMath.Equal(total, shareCount);
        var amount = distribution.At(oneBasedOrdinal);
        if (amount == 0)
            throw new BadRequestException("Each reviewed equal share must contain at least one minor unit.");
        var previousCount = oneBasedOrdinal - 1L;
        var prefixMinor = checked(total / shareCount * previousCount
            + Math.Min(total % shareCount, previousCount));
        var remainingScope = prefixMinor == 0 ? reviewedScope
            : AccountDebtMath.Subtract(reviewedScope, AccountDebtMath.Amount(reviewedScope, prefixMinor));
        return AccountDebtMath.Amount(remainingScope, amount);
    }
}
