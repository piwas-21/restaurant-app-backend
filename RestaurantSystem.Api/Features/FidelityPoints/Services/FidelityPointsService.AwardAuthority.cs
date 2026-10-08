using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.AccountPayments.Services;
using RestaurantSystem.Api.Features.OrderAmendments.Dtos;
using RestaurantSystem.Api.Features.OrderAmendments.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.FidelityPoints.Services;

public partial class FidelityPointsService
{
    private enum AwardSuppressionValidationMode
    {
        RecordedRowsOnly,
        CompletePreAwardCoverage
    }

    private sealed record AwardAmendmentScope(
        Guid Id, Guid SourceOrderId, OrderAmendmentState State, string ChangesJson);

    private async Task<bool> TryLockAwardUserAsync(Guid userId, CancellationToken cancellationToken)
    {
        var locked = await _context.Database.SqlQuery<Guid>(
                $"SELECT id AS \"Value\" FROM \"Users\" WHERE id = {userId} AND is_deleted = FALSE FOR NO KEY UPDATE")
            .Take(2)
            .ToListAsync(cancellationToken);
        return locked.Count == 1 && locked[0] == userId;
    }

    private async Task<OrderBillingSnapshotOwnerLink?> ReadEarningOwnerLinkAsync(
        Guid orderId, CancellationToken cancellationToken)
    {
        var links = await _context.OrderBillingSnapshotOwnerLinks.AsNoTracking()
            .Where(value => value.OrderId == orderId && value.Slot == OrderBillingSnapshotOwnerSlot.Earning)
            .Take(2)
            .ToListAsync(cancellationToken);
        if (links.Count > 1)
            throw new ConflictException("The accepted order has duplicate earning owner links.");
        return links.SingleOrDefault();
    }

    private async Task<OrderBillingSnapshotOwnerLink?> LockEarningOwnerLinkAsync(
        Guid orderId, CancellationToken cancellationToken)
    {
        var links = await _context.OrderBillingSnapshotOwnerLinks
            .FromSqlInterpolated($"SELECT * FROM order_billing_snapshot_owner_links WHERE order_id = {orderId} AND slot = 'Earning' FOR UPDATE")
            .AsNoTracking()
            .Take(2)
            .ToListAsync(cancellationToken);
        if (links.Count > 1)
            throw new ConflictException("The accepted order has duplicate earning owner links.");
        return links.SingleOrDefault();
    }

    private static bool IsLinkedOwner(OrderBillingSnapshotOwnerLink? link, Guid? expectedUserId) =>
        expectedUserId.HasValue && link is not null
        && link.Slot == OrderBillingSnapshotOwnerSlot.Earning
        && link.Disposition == OrderBillingSnapshotOwnerDisposition.Linked
        && link.UserId == expectedUserId
        && !link.ErasedAt.HasValue
        && link.ErasureTransactionId is null;

    private async Task<List<OrderBillingSnapshotUnit>> ReadSnapshotUnitsAsync(
        Guid orderId, CancellationToken cancellationToken) => await _context.OrderBillingSnapshotUnits
        .AsNoTracking()
        .Where(value => value.OrderId == orderId)
        .OrderBy(value => value.OrderItemId)
        .ThenBy(value => value.UnitOrdinal)
        .ToListAsync(cancellationToken);

    private async Task<List<OrderBillingUnitAwardSuppression>> ReadUnitSuppressionsAsync(
        Guid orderId, CancellationToken cancellationToken) => await _context.OrderBillingUnitAwardSuppressions
        .AsNoTracking()
        .Where(value => value.OrderId == orderId)
        .OrderBy(value => value.SnapshotUnitId)
        .ToListAsync(cancellationToken);

    private async Task<bool> HasCommittedRemovalAmendmentAsync(
        Guid orderId, CancellationToken cancellationToken)
    {
        var changesJson = await _context.OrderAmendments.AsNoTracking()
            .Where(value => value.SourceOrderId == orderId
                && value.State == OrderAmendmentState.Committed)
            .Select(value => value.ChangesJson)
            .ToListAsync(cancellationToken);
        foreach (var json in changesJson)
        {
            try
            {
                if (OrderAmendmentJson.Deserialize<List<OrderAmendmentChangeSnapshot>>(json)
                    .Any(value => value is null
                        || value.Kind is OrderAmendmentChangeKind.Void or OrderAmendmentChangeKind.Replace))
                    return true;
            }
            catch (JsonException)
            {
                return true;
            }
        }
        return false;
    }

    private void AddAwardUnitCoverage(
        Guid orderId, Guid witnessId, IReadOnlyList<OrderBillingSnapshotUnit> units,
        IReadOnlyCollection<OrderBillingUnitAwardSuppression>? suppressions = null)
    {
        var suppressedIds = suppressions?.Select(value => value.SnapshotUnitId).ToHashSet() ?? [];
        var covered = units.Where(value => value.OrderId == orderId && value.EarnedPoints > 0
            && !suppressedIds.Contains(value.Id)).ToArray();
        var total = covered.Sum(value => (long)value.EarnedPoints);
        var expected = units.Sum(value => (long)value.EarnedPoints) -
            (suppressions?.Sum(value => (long)value.SuppressedEarnedPoints) ?? 0);
        if (covered.Any(value => value.Id == Guid.Empty || value.OrderItemId == Guid.Empty
                || value.UnitOrdinal < 1)
            || total != expected)
            throw new ConflictException("The awarded units do not match the exact unsuppressed earning allocation.");
        _context.OrderBillingAwardUnitCoverages.AddRange(covered.Select(value =>
            new OrderBillingAwardUnitCoverage
            {
                Id = Guid.NewGuid(),
                OrderId = orderId,
                AwardWitnessId = witnessId,
                SnapshotUnitId = value.Id,
                EligibleEarnedPoints = value.EarnedPoints,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = AwardJournalAuditIdentifier
            }));
    }

    private static int ValidateSnapshotCandidate(AwardOrderState order, OrderBillingSnapshot snapshot)
    {
        var candidate = snapshot.EarnedPointsCandidate
            ?? throw new ConflictException("An unevaluated earning snapshot cannot be awarded.");
        var disposition = snapshot.EffectiveEarningDisposition;
        if (disposition != OrderBillingEarningDisposition.Evaluated
            || candidate < 0 || order.FidelityPointsEarned != candidate
            || snapshot.EarningBasisMinor < 0
            || string.IsNullOrWhiteSpace(snapshot.EarningEvaluationVersion)
            || !IsRuleFingerprint(snapshot.EarningRuleSetFingerprint))
            throw new ConflictException("The accepted order does not match its frozen earning evaluation.");

        var hasRule = snapshot.EarningRuleId.HasValue;
        if (candidate > 0 && (!hasRule || snapshot.EarningRulePoints != candidate))
            throw new ConflictException("The positive earning candidate has no matching frozen rule.");
        if (hasRule && (string.IsNullOrWhiteSpace(snapshot.EarningRuleName)
            || !snapshot.EarningRuleMinimumMinor.HasValue
            || snapshot.EarningRuleMinimumMinor < 0
            || snapshot.EarningRulePoints != candidate
            || (snapshot.EarningRuleMaximumMinor.HasValue
                && snapshot.EarningRuleMaximumMinor < snapshot.EarningRuleMinimumMinor)))
            throw new ConflictException("The frozen earning rule facts are inconsistent.");
        if (!hasRule && (snapshot.EarningRuleName is not null
            || snapshot.EarningRuleMinimumMinor.HasValue
            || snapshot.EarningRuleMaximumMinor.HasValue
            || snapshot.EarningRulePoints.HasValue
            || snapshot.EarningRulePriority.HasValue))
            throw new ConflictException("The no-match earning evaluation has partial rule facts.");
        return candidate;
    }

    private static void ValidateUnitAllocation(int candidate, IReadOnlyList<OrderBillingSnapshotUnit> units)
    {
        long total = 0;
        var identities = new HashSet<(Guid ItemId, int Ordinal)>();
        foreach (var unit in units)
        {
            if (unit.Id == Guid.Empty || unit.OrderId == Guid.Empty || unit.OrderItemId == Guid.Empty
                || unit.UnitOrdinal < 1 || unit.EarnedPoints < 0
                || !identities.Add((unit.OrderItemId, unit.UnitOrdinal)))
                throw new ConflictException("The frozen earning units contain invalid or duplicate rows.");
            total = checked(total + unit.EarnedPoints);
        }
        if (total != candidate)
            throw new ConflictException("The frozen per-unit earning points do not sum to the accepted candidate.");
    }

    private static decimal SnapshotEarningTotal(OrderBillingSnapshot snapshot)
    {
        try
        {
            return new AccountMoney(snapshot.Currency).ToMajor(snapshot.EarningBasisMinor);
        }
        catch (BadRequestException)
        {
            throw new ConflictException("The accepted snapshot currency cannot represent its earning basis.");
        }
    }

    private static bool IsRuleFingerprint(string? value) => value is { Length: 64 }
        && Regex.IsMatch(value, "^[0-9a-f]{64}$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
}
