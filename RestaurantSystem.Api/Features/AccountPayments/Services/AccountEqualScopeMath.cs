using RestaurantSystem.Api.Common.Exceptions;

namespace RestaurantSystem.Api.Features.AccountPayments.Services;

/// <summary>Each share retains its original reviewed scope, regardless of payment order.</summary>
internal static class AccountEqualScopeMath
{
    internal static IReadOnlyList<AccountDebtSegment> ForShare(
        IReadOnlyList<AccountDebtSegment> reviewedScope, int shareCount, int oneBasedOrdinal,
        long roundingIncrementMinor = 1)
    {
        var total = AccountDebtMath.Total(reviewedScope);
        var distribution = AccountShareMath.Equal(total, shareCount, roundingIncrementMinor);
        var amount = distribution.At(oneBasedOrdinal);
        if (amount == 0)
            throw new BadRequestException("Each reviewed equal share must contain at least one minor unit.");
        var prefixMinor = distribution.Before(oneBasedOrdinal);
        var remainingScope = prefixMinor == 0 ? reviewedScope
            : AccountDebtMath.Subtract(reviewedScope, AccountDebtMath.Amount(reviewedScope, prefixMinor));
        return AccountDebtMath.Amount(remainingScope, amount);
    }
}
