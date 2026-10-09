using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.FidelityPoints.Models;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.FidelityPoints.Services;

public partial class FidelityPointsService
{
    private sealed record ReplayOwner(Guid UserId);

    private async Task<FidelityPointsAwardResult> ReplayLegacyAwardAsync(
        AwardOrderState order,
        List<FidelityPointsTransaction> existingAwards,
        CancellationToken cancellationToken)
    {
        if (existingAwards.Count == 0)
            return Deferred(FidelityPointsAwardDeferralReason.MissingSnapshot);

        var original = existingAwards[0];
        if (!order.UserId.HasValue || order.FidelityPointsEarned <= 0
            || !MatchesAward(original, order, order.UserId.Value, order.FidelityPointsEarned, order.SubTotal))
            throw new ConflictException("The legacy loyalty award does not exactly match its persisted order.");
        if (!await TryLockAwardUserAsync(order.UserId.Value, cancellationToken))
            return Deferred(FidelityPointsAwardDeferralReason.EarningOwnerUnavailable);
        if (await LockBalanceRowForLoyaltyAsync(order.UserId.Value, cancellationToken) is null)
            throw new ConflictException("The original loyalty award has no balance record.");
        return Result(FidelityPointsAwardDisposition.AlreadyAwarded,
            original.Points, original.Points, 0);
    }

    private async Task<FidelityPointsAwardResult> ReplayLegacyWithoutSnapshotAsync(
        AwardOrderState order, List<FidelityPointsTransaction> existingAwards,
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction? transaction,
        CancellationToken cancellationToken)
    {
        var result = await ReplayLegacyAwardAsync(order, existingAwards, cancellationToken);
        await CommitOwnedTransactionAsync(transaction, cancellationToken);
        if (result.Disposition != FidelityPointsAwardDisposition.Deferred)
            return result;

        var ineligible = ReadIneligibleReason(order);
        if (ineligible.HasValue)
            return Deferred(ineligible.Value);
        if (await IsProviderManagedAsync(order.Id, cancellationToken))
            return Deferred(FidelityPointsAwardDeferralReason.ProviderManaged);
        return result;
    }

    private async Task<FidelityPointsAwardResult> ReplayWitnessAsync(
        OrderBillingAwardWitness witness, AwardOrderState order, CancellationToken cancellationToken)
    {
        var snapshot = await _context.OrderBillingSnapshots.AsNoTracking()
            .SingleOrDefaultAsync(value => value.OrderId == order.Id, cancellationToken)
            ?? throw new ConflictException("The loyalty award witness has no accepted billing snapshot.");
        var candidate = ValidateSnapshotCandidate(order, snapshot);
        var owner = await LockReplayOwnerAsync(witness, order, cancellationToken);
        if (owner is null)
            return Deferred(FidelityPointsAwardDeferralReason.EarningOwnerUnavailable);

        var units = await ReadSnapshotUnitsAsync(order.Id, cancellationToken);
        ValidateUnitAllocation(candidate, units);
        var suppressions = await ReadUnitSuppressionsAsync(order.Id, cancellationToken);
        var suppressed = await ValidateSuppressionsAsync(order.Id, units, suppressions, _context,
            AwardSuppressionValidationMode.RecordedRowsOnly, cancellationToken);
        var coverage = await _context.OrderBillingAwardUnitCoverages.AsNoTracking()
            .Where(value => value.OrderId == order.Id)
            .ToListAsync(cancellationToken);
        ValidateAwardUnitCoverage(witness, units, coverage);
        ValidateWitnessTotals(witness, candidate, suppressed);
        if (witness.Outcome == OrderBillingAwardOutcome.Awarded)
            return await ReplayAwardedWitnessAsync(witness, order, snapshot, owner, candidate, suppressed,
                cancellationToken);
        return await ReplayNoAwardWitnessAsync(witness, order.Id, candidate, suppressed, cancellationToken);
    }

    private async Task<ReplayOwner?> LockReplayOwnerAsync(
        OrderBillingAwardWitness witness, AwardOrderState order, CancellationToken cancellationToken)
    {
        var link = await LockEarningOwnerLinkAsync(order.Id, cancellationToken);
        if (link is null || link.Id != witness.OwnerLinkId)
            throw new ConflictException("The loyalty award witness does not match its earning owner link.");
        if (!IsLinkedOwner(link, order.UserId) || link.UserId is not Guid userId)
            return null;
        if (!await TryLockAwardUserAsync(userId, cancellationToken))
            return null;
        if (!IsLinkedOwner(link, order.UserId) || link.Id != witness.OwnerLinkId)
            return null;
        return new(userId);
    }

    private static void ValidateWitnessTotals(
        OrderBillingAwardWitness witness, int candidate, int suppressed)
    {
        if (witness.CandidatePoints != candidate || witness.AppliedPoints < 0
            || witness.SuppressedPoints < 0
            || (long)witness.AppliedPoints + witness.SuppressedPoints != candidate
            || witness.SuppressedPoints != suppressed)
            throw new ConflictException("The loyalty award witness does not reconcile with its frozen unit history.");
        if (witness.Outcome != ReadAwardOutcome(witness.AppliedPoints, candidate))
            throw new ConflictException("The loyalty award witness has an invalid outcome.");
    }

    private async Task<FidelityPointsAwardResult> ReplayAwardedWitnessAsync(
        OrderBillingAwardWitness witness, AwardOrderState order, OrderBillingSnapshot snapshot,
        ReplayOwner owner, int candidate, int suppressed, CancellationToken cancellationToken)
    {
        if (!witness.EarnedTransactionId.HasValue)
            throw new ConflictException("The awarded loyalty witness has no transaction lineage.");
        var earned = await _context.FidelityPointsTransactions.AsNoTracking()
            .SingleOrDefaultAsync(value => value.Id == witness.EarnedTransactionId.Value, cancellationToken)
            ?? throw new ConflictException("The witnessed loyalty transaction is missing.");
        var earnedRows = await ReadEarnedRowsAsync(order.Id, cancellationToken);
        if (earnedRows.Count != 1 || earnedRows[0].Id != earned.Id)
            throw new ConflictException("The witnessed order has duplicate or mismatched earned ledger history.");
        if (!MatchesAward(earned, order, owner.UserId,
                witness.AppliedPoints, SnapshotEarningTotal(snapshot)))
            throw new ConflictException("The witnessed loyalty transaction does not match its frozen award.");
        if (await LockBalanceRowForLoyaltyAsync(owner.UserId, cancellationToken) is null)
            throw new ConflictException("The witnessed loyalty award has no balance record.");
        return Result(FidelityPointsAwardDisposition.AlreadyAwarded,
            candidate, witness.AppliedPoints, suppressed);
    }

    private async Task<FidelityPointsAwardResult> ReplayNoAwardWitnessAsync(
        OrderBillingAwardWitness witness, Guid orderId, int candidate, int suppressed,
        CancellationToken cancellationToken)
    {
        if (witness.EarnedTransactionId.HasValue
            || await _context.FidelityPointsTransactions.AsNoTracking().AnyAsync(
                value => value.OrderId == orderId && value.TransactionType == TransactionType.Earned,
                cancellationToken))
            throw new ConflictException("A no-award witness conflicts with earned ledger history.");
        var disposition = witness.Outcome == OrderBillingAwardOutcome.EvaluatedZero
            ? FidelityPointsAwardDisposition.EvaluatedZero
            : FidelityPointsAwardDisposition.FullySuppressed;
        return Result(disposition, candidate, 0, suppressed);
    }

    private static FidelityPointsAwardResult Deferred(FidelityPointsAwardDeferralReason reason) =>
        new(FidelityPointsAwardDisposition.Deferred, reason, null, null, null);

    private static void ValidateAwardUnitCoverage(
        OrderBillingAwardWitness witness,
        IReadOnlyCollection<OrderBillingSnapshotUnit> units,
        List<OrderBillingAwardUnitCoverage> coverage)
    {
        if (coverage.Count == 0)
            return; // Historical partial awards remain replayable but cannot authorize compensation.
        var unitsById = units.ToDictionary(value => value.Id);
        if (coverage.Any(value => value.OrderId != witness.OrderId
                || value.AwardWitnessId != witness.Id || value.SnapshotUnitId == Guid.Empty
                || value.EligibleEarnedPoints <= 0
                || !unitsById.TryGetValue(value.SnapshotUnitId, out var unit)
                || unit.EarnedPoints != value.EligibleEarnedPoints)
            || coverage.Select(value => value.SnapshotUnitId).Distinct().Count() != coverage.Count
            || coverage.Sum(value => (long)value.EligibleEarnedPoints) != witness.AppliedPoints)
            throw new ConflictException("The loyalty award unit coverage does not match its immutable witness.");
    }

    private static FidelityPointsAwardResult Result(
        FidelityPointsAwardDisposition disposition, int candidate, int applied, int suppressed) =>
        new(disposition, null, candidate, applied, suppressed);

    private static OrderBillingAwardOutcome ReadAwardOutcome(int appliedPoints, int candidatePoints)
    {
        if (appliedPoints > 0)
            return OrderBillingAwardOutcome.Awarded;
        return candidatePoints == 0
            ? OrderBillingAwardOutcome.EvaluatedZero
            : OrderBillingAwardOutcome.FullySuppressed;
    }

    private static OrderBillingAwardWitness NewWitness(
        Guid orderId, Guid ownerLinkId, int candidate, int applied, int suppressed,
        OrderBillingAwardOutcome outcome, Guid? earnedTransactionId) => new()
        {
            Id = Guid.NewGuid(),
            OrderId = orderId,
            OwnerLinkId = ownerLinkId,
            Outcome = outcome,
            CandidatePoints = candidate,
            AppliedPoints = applied,
            SuppressedPoints = suppressed,
            EarnedTransactionId = earnedTransactionId,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = AwardJournalAuditIdentifier
        };

    private static FidelityPointsTransaction NewEarnedTransaction(
        AwardOrderState order, Guid userId, int points, decimal orderTotal,
        DateTime now, string auditIdentifier) => new()
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            OrderId = order.Id,
            TransactionType = TransactionType.Earned,
            Points = points,
            OrderTotal = orderTotal,
            Description = "Points earned from order",
            CreatedAt = now,
            CreatedBy = auditIdentifier
        };

    private void ApplyAwardBalance(
        FidelityPointBalance? balance, Guid userId, int points, DateTime now, string auditIdentifier)
    {
        if (balance is null)
        {
            _context.FidelityPointBalances.Add(new FidelityPointBalance
            {
                UserId = userId,
                CurrentPoints = points,
                TotalEarnedPoints = points,
                TotalRedeemedPoints = 0,
                LastUpdated = now,
                CreatedAt = now,
                CreatedBy = auditIdentifier
            });
            return;
        }

        if (balance.CurrentPoints < 0)
            throw new ConflictException("The loyalty balance is negative and requires reconciliation.");
        var updatedCurrentPoints = checked(balance.CurrentPoints + points);
        var updatedTotalEarnedPoints = checked(balance.TotalEarnedPoints + points);
        balance.CurrentPoints = updatedCurrentPoints;
        balance.TotalEarnedPoints = updatedTotalEarnedPoints;
        balance.LastUpdated = now;
        balance.UpdatedAt = now;
        balance.UpdatedBy = auditIdentifier;
    }

}
