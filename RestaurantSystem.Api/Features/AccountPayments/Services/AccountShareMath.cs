using System.Numerics;
using RestaurantSystem.Api.Common.Exceptions;

namespace RestaurantSystem.Api.Features.AccountPayments.Services;

/// <summary>Exact minor-unit distribution; stable positions receive any indivisible remainder.</summary>
internal static class AccountShareMath
{
    internal static AccountShareDistribution Equal(long totalMinor, int count)
    {
        if (totalMinor < 0 || count <= 0)
            throw new BadRequestException("A share distribution requires a nonnegative balance and positive count.");
        return new AccountShareDistribution(totalMinor, count);
    }

    /// <summary>Allocates a frozen amount by nonnegative weights, using stable largest remainders.</summary>
    internal static IReadOnlyList<long> Weighted(long totalMinor, IReadOnlyList<long> weights)
    {
        if (totalMinor < 0 || weights.Count == 0 || weights.Any(weight => weight < 0))
            throw new BadRequestException("An allocation requires a nonnegative amount and nonnegative weights.");
        var denominator = weights.Aggregate(BigInteger.Zero, (sum, weight) => sum + weight);
        if (denominator.IsZero)
            throw new BadRequestException("At least one allocation weight must be positive.");

        var amounts = new long[weights.Count];
        var remainders = new BigInteger[weights.Count];
        var allocated = 0L;
        for (var index = 0; index < weights.Count; index++)
        {
            var quotient = BigInteger.DivRem((BigInteger)totalMinor * weights[index], denominator,
                out remainders[index]);
            amounts[index] = (long)quotient;
            allocated += amounts[index];
        }
        var extras = totalMinor - allocated;
        foreach (var index in Enumerable.Range(0, weights.Count)
            .OrderByDescending(index => remainders[index]).ThenBy(index => index).Take((int)extras))
            amounts[index]++;
        return amounts;
    }
}

/// <summary>Compact ordinal range; even extreme quantities do not allocate one object per unit.</summary>
internal sealed record AccountShareDistribution(long TotalMinor, int Count)
{
    internal long At(int oneBasedOrdinal)
    {
        if (oneBasedOrdinal <= 0 || oneBasedOrdinal > Count)
            throw new BadRequestException("The selected share ordinal is outside the reviewed distribution.");
        return TotalMinor / Count + (oneBasedOrdinal <= TotalMinor % Count ? 1 : 0);
    }
}
