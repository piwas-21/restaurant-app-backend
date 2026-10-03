using System.Numerics;
using RestaurantSystem.Api.Common.Exceptions;

namespace RestaurantSystem.Api.Features.AccountPayments.Services;

/// <summary>Conserves frozen charges and contributions without expanding quantities into units.</summary>
internal static class AccountDebtMath
{
    /// <summary>Legacy debt without frozen lines remains an amount, without inventing item ownership.</summary>
    internal static IReadOnlyList<AccountDebtSegment> CreateUnitemized(
        Guid orderId, long chargeMinor, long historicalContributionMinor)
    {
        if (orderId == Guid.Empty || chargeMinor < 0 || historicalContributionMinor < 0
            || historicalContributionMinor > chargeMinor)
            throw new BadRequestException("The historical order balance is invalid.");
        var remaining = chargeMinor - historicalContributionMinor;
        return remaining == 0 ? [] : [new(orderId, null, 1, 1, remaining)];
    }

    internal static IReadOnlyList<AccountDebtSegment> CreateLine(
        Guid orderId, Guid itemId, int quantity, long chargeMinor, long historicalContributionMinor)
    {
        if (orderId == Guid.Empty || itemId == Guid.Empty || quantity <= 0
            || chargeMinor < 0 || historicalContributionMinor < 0 || historicalContributionMinor > chargeMinor)
            throw new BadRequestException("The frozen line and its contribution must form a valid account balance.");
        var distribution = AccountShareMath.Equal(chargeMinor, quantity);
        var extraUnits = (int)(chargeMinor % quantity);
        var segments = new List<AccountDebtSegment>(2);
        if (extraUnits > 0)
            segments.Add(new(orderId, itemId, 1, extraUnits, distribution.At(1)));
        if (quantity > extraUnits && chargeMinor / quantity > 0)
            segments.Add(new(orderId, itemId, extraUnits + 1, quantity - extraUnits, chargeMinor / quantity));
        return RemovePrefixContribution(segments, historicalContributionMinor);
    }

    /// <summary>Subtracts settled or reserved amounts per unit; ownership remains a separate ledger fact.</summary>
    internal static IReadOnlyList<AccountDebtSegment> Subtract(
        IReadOnlyList<AccountDebtSegment> due, IReadOnlyList<AccountDebtSegment> reductions)
    {
        Validate(due, requireDisjoint: true);
        Validate(reductions);
        var result = new List<AccountDebtSegment>();
        foreach (var segment in due)
        {
            var matching = reductions.Where(reduction => reduction.OrderId == segment.OrderId
                && reduction.OrderItemId == segment.OrderItemId
                && reduction.StartOrdinal < segment.EndExclusive
                && reduction.EndExclusive > segment.StartOrdinal).ToList();
            var boundaries = matching.SelectMany(reduction => new[]
                {
                    Math.Max(segment.StartOrdinal, reduction.StartOrdinal),
                    Math.Min(segment.EndExclusive, reduction.EndExclusive)
                })
                .Append(segment.StartOrdinal).Append(segment.EndExclusive).Distinct().Order().ToArray();
            for (var index = 0; index < boundaries.Length - 1; index++)
            {
                var start = boundaries[index];
                var applied = matching.Where(reduction => reduction.StartOrdinal <= start
                    && reduction.EndExclusive > start)
                    .Aggregate(BigInteger.Zero, (sum, reduction) => sum + reduction.MinorPerUnit);
                if (applied > segment.MinorPerUnit)
                    throw new ConflictException("Payment allocations exceed a frozen unit balance. Reconciliation is required.");
                var left = segment.MinorPerUnit - (long)applied;
                if (left > 0)
                    result.Add(segment with
                    {
                        StartOrdinal = checked((int)start),
                        Count = checked((int)(boundaries[index + 1] - start)),
                        MinorPerUnit = left
                    });
            }
        }
        // Reject a reduction that targets debt absent from this snapshot, including a fully paid unit.
        foreach (var reduction in reductions)
        {
            var covered = due.Where(segment => segment.OrderId == reduction.OrderId
                && segment.OrderItemId == reduction.OrderItemId)
                .Sum(segment => Math.Max(0L, Math.Min(segment.EndExclusive, reduction.EndExclusive)
                    - Math.Max(segment.StartOrdinal, reduction.StartOrdinal)));
            if (covered != reduction.Count)
                throw new ConflictException("A payment allocation no longer belongs to the available account scope.");
        }
        return result;
    }

    /// <summary>Allocates an amount in reviewed order, retaining partial units as contributions.</summary>
    internal static IReadOnlyList<AccountDebtSegment> Amount(
        IReadOnlyList<AccountDebtSegment> available, long amountMinor)
    {
        Validate(available, requireDisjoint: true);
        if (amountMinor <= 0 || amountMinor > Total(available))
            throw new BadRequestException("The contribution must fit the available account balance.");
        var result = new List<AccountDebtSegment>();
        var left = amountMinor;
        foreach (var segment in available)
        {
            if (left == 0) break;
            var fullUnits = (int)Math.Min(segment.Count, left / segment.MinorPerUnit);
            if (fullUnits > 0)
            {
                result.Add(segment with { Count = fullUnits });
                left -= checked(fullUnits * segment.MinorPerUnit);
            }
            if (fullUnits < segment.Count && left > 0)
            {
                result.Add(segment with { StartOrdinal = segment.StartOrdinal + fullUnits, Count = 1, MinorPerUnit = left });
                left = 0;
            }
        }
        return result;
    }

    internal static IReadOnlyList<AccountDebtSegment> Items(
        IReadOnlyList<AccountDebtSegment> available, IReadOnlyList<AccountUnitIdentity> selected)
    {
        Validate(available, requireDisjoint: true);
        if (selected.Count == 0 || selected.Distinct().Count() != selected.Count)
            throw new BadRequestException("Select distinct payable item units.");
        var result = new List<AccountDebtSegment>(selected.Count);
        foreach (var unit in selected)
        {
            var match = available.SingleOrDefault(segment => segment.OrderId == unit.OrderId
                && segment.OrderItemId == unit.OrderItemId && segment.StartOrdinal <= unit.Ordinal
                && segment.EndExclusive > unit.Ordinal);
            if (match is null)
                throw new ConflictException("A selected item is already paid or reserved. Refresh the account.");
            result.Add(match with { StartOrdinal = unit.Ordinal, Count = 1 });
        }
        return result;
    }

    internal static long Total(IEnumerable<AccountDebtSegment> segments) =>
        segments.Aggregate(0L, (sum, segment) => checked(sum + segment.TotalMinor));

    private static IReadOnlyList<AccountDebtSegment> RemovePrefixContribution(
        IReadOnlyList<AccountDebtSegment> segments, long contributionMinor)
    {
        if (contributionMinor == 0) return segments;
        return Subtract(segments, Amount(segments, contributionMinor));
    }

    private static void Validate(IReadOnlyList<AccountDebtSegment> segments, bool requireDisjoint = false)
    {
        foreach (var segment in segments)
            if (segment.OrderId == Guid.Empty || segment.OrderItemId == Guid.Empty || segment.StartOrdinal < 1
                || segment.Count <= 0 || segment.EndExclusive > (long)int.MaxValue + 1 || segment.MinorPerUnit <= 0
                || (segment.OrderItemId is null && (segment.StartOrdinal != 1 || segment.Count != 1)))
                throw new BadRequestException("A payment scope contains an invalid unit range.");
        if (!requireDisjoint) return;
        foreach (var group in segments.GroupBy(segment => (segment.OrderId, segment.OrderItemId)))
        {
            var end = 0L;
            foreach (var segment in group.OrderBy(segment => segment.StartOrdinal))
            {
                if (segment.StartOrdinal < end)
                    throw new BadRequestException("The available payment scope contains overlapping units.");
                end = segment.EndExclusive;
            }
        }
    }
}
