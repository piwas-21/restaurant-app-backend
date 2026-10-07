using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.FidelityPoints.Models;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.FidelityPoints.Services;

public partial class FidelityPointsService
{
    private const string AwardJournalAuditIdentifier = "OrderBillingAwardBoundary";

    public async Task<FidelityPointsAwardResult> AwardAcceptedOrderAsync(
        Guid orderId, CancellationToken cancellationToken = default)
    {
        var ownsTransaction = _context.Database.CurrentTransaction is null;
        var transaction = ownsTransaction
            ? await _context.Database.BeginTransactionAsync(cancellationToken)
            : null;
        try
        {
            var order = await LockAwardOrderAsync(orderId, cancellationToken);
            if (order is null)
                return Deferred(FidelityPointsAwardDeferralReason.OrderUnavailable);

            var witness = await ReadAwardWitnessAsync(orderId, cancellationToken);
            if (witness is not null)
            {
                var replay = await ReplayWitnessAsync(witness, order, cancellationToken);
                if (transaction is not null)
                    await transaction.CommitAsync(cancellationToken);
                return replay;
            }

            var snapshot = await _context.OrderBillingSnapshots.AsNoTracking()
                .SingleOrDefaultAsync(value => value.OrderId == orderId, cancellationToken);
            var existingAwards = await ReadEarnedRowsAsync(orderId, cancellationToken);
            if (existingAwards.Count > 1)
                throw new ConflictException("Duplicate loyalty awards require reconciliation.");

            if (snapshot is null)
            {
                var legacyReplay = await ReplayLegacyAwardAsync(order, existingAwards, cancellationToken);
                if (transaction is not null)
                    await transaction.CommitAsync(cancellationToken);
                if (legacyReplay.Disposition == FidelityPointsAwardDisposition.Deferred)
                {
                    var missingSnapshotReason = ReadIneligibleReason(order);
                    if (missingSnapshotReason.HasValue)
                        return Deferred(missingSnapshotReason.Value);
                    if (await IsProviderManagedAsync(orderId, cancellationToken))
                        return Deferred(FidelityPointsAwardDeferralReason.ProviderManaged);
                }
                return legacyReplay;
            }

            if (!snapshot.EarnedPointsCandidate.HasValue)
            {
                if (order.FidelityPointsEarned != 0 || HasPartialEarningEvidence(snapshot))
                    throw new ConflictException("The unevaluated earning snapshot contains partial or inconsistent rule evidence.");
                return Deferred(FidelityPointsAwardDeferralReason.CandidateUnevaluated);
            }

            var candidate = ValidateSnapshotCandidate(order, snapshot);
            var ownerLink = await LockEarningOwnerLinkAsync(orderId, cancellationToken);
            if (!IsLinkedOwner(ownerLink, order.UserId))
                return Deferred(FidelityPointsAwardDeferralReason.EarningOwnerUnavailable);
            if (!await TryLockAwardUserAsync(order.UserId!.Value, cancellationToken))
                return Deferred(FidelityPointsAwardDeferralReason.EarningOwnerUnavailable);
            if (existingAwards.Count == 0)
            {
                var ineligible = ReadIneligibleReason(order);
                if (ineligible.HasValue)
                    return Deferred(ineligible.Value);
                if (await IsProviderManagedAsync(orderId, cancellationToken))
                    return Deferred(FidelityPointsAwardDeferralReason.ProviderManaged);
            }

            var units = await ReadSnapshotUnitsAsync(orderId, cancellationToken);
            ValidateUnitAllocation(candidate, units);
            var suppressions = await ReadUnitSuppressionsAsync(orderId, cancellationToken);
            var suppressed = await ValidateSuppressionsAsync(orderId, units, suppressions, _context,
                AwardSuppressionValidationMode.CompletePreAwardCoverage, cancellationToken);
            if (suppressed > candidate)
                throw new ConflictException("The loyalty removal history exceeds the frozen earning candidate.");
            var applied = checked(candidate - suppressed);
            var earningTotal = SnapshotEarningTotal(snapshot);

            if (existingAwards.Count == 1)
            {
                var existing = existingAwards[0];
                if (suppressed != 0 || applied != candidate
                    || !MatchesAward(existing, order, ownerLink!.UserId!.Value, candidate, earningTotal))
                    throw new ConflictException("The original loyalty award does not match the frozen snapshot.");
                if (await HasCommittedRemovalAmendmentAsync(orderId, cancellationToken))
                    throw new ConflictException("A legacy award with accepted removals has no provable per-unit award coverage.");
                if (await LockBalanceRowForLoyaltyAsync(ownerLink.UserId.Value, cancellationToken) is null)
                    throw new ConflictException("The original loyalty award has no balance record.");

                var backfilled = NewWitness(orderId, ownerLink.Id, candidate, candidate, 0,
                    OrderBillingAwardOutcome.Awarded, existing.Id);
                _context.OrderBillingAwardWitnesses.Add(backfilled);
                AddAwardUnitCoverage(orderId, backfilled.Id, units);
                await _context.SaveChangesAsync(cancellationToken);
                if (transaction is not null)
                    await transaction.CommitAsync(cancellationToken);
                return Result(FidelityPointsAwardDisposition.AlreadyAwarded, candidate, candidate, 0);
            }

            if (applied == 0)
            {
                var outcome = candidate == 0
                    ? OrderBillingAwardOutcome.EvaluatedZero
                    : OrderBillingAwardOutcome.FullySuppressed;
                _context.OrderBillingAwardWitnesses.Add(NewWitness(
                    orderId, ownerLink!.Id, candidate, 0, suppressed, outcome, null));
                await _context.SaveChangesAsync(cancellationToken);
                if (transaction is not null)
                    await transaction.CommitAsync(cancellationToken);
                return Result(candidate == 0
                    ? FidelityPointsAwardDisposition.EvaluatedZero
                    : FidelityPointsAwardDisposition.FullySuppressed, candidate, 0, suppressed);
            }

            var balance = await LockBalanceRowForLoyaltyAsync(ownerLink!.UserId!.Value, cancellationToken);
            var now = DateTime.UtcNow;
            var auditIdentifier = _currentUserService.GetAuditIdentifier();
            ApplyAwardBalance(balance, ownerLink.UserId.Value, applied, now, auditIdentifier);
            var earned = NewEarnedTransaction(order, ownerLink.UserId.Value, applied, earningTotal, now, auditIdentifier);
            _context.FidelityPointsTransactions.Add(earned);
            var newWitness = NewWitness(
                orderId, ownerLink.Id, candidate, applied, suppressed,
                OrderBillingAwardOutcome.Awarded, earned.Id);
            _context.OrderBillingAwardWitnesses.Add(newWitness);
            AddAwardUnitCoverage(orderId, newWitness.Id, units, suppressions);
            await _context.SaveChangesAsync(cancellationToken);
            if (transaction is not null)
                await transaction.CommitAsync(cancellationToken);
            return Result(FidelityPointsAwardDisposition.Awarded, candidate, applied, suppressed);
        }
        catch
        {
            if (transaction is not null)
                await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
        finally
        {
            if (transaction is not null)
                await transaction.DisposeAsync();
        }
    }

    private async Task<AwardOrderState?> LockAwardOrderAsync(Guid orderId, CancellationToken cancellationToken)
    {
        var rows = await _context.Orders
            .FromSqlInterpolated($"SELECT * FROM orders WHERE id = {orderId} AND is_deleted = FALSE FOR UPDATE")
            .AsNoTracking()
            .Select(value => new AwardOrderState(
                value.Id, value.UserId, value.Status, value.PaymentStatus,
                value.FidelityPointsEarned, value.SubTotal, value.OrderNumber))
            .Take(2)
            .ToListAsync(cancellationToken);
        if (rows.Count > 1)
            throw new ConflictException("The order identity is ambiguous for loyalty award.");
        return rows.SingleOrDefault();
    }

    private static FidelityPointsAwardDeferralReason? ReadIneligibleReason(AwardOrderState order)
    {
        if (order.Status is OrderStatus.Cancelled or OrderStatus.Refunded)
            return FidelityPointsAwardDeferralReason.OrderCancelled;
        return order.PaymentStatus is PaymentStatus.Completed or PaymentStatus.Overpaid
            ? null
            : FidelityPointsAwardDeferralReason.PaymentNotSettled;
    }

    private static bool HasPartialEarningEvidence(OrderBillingSnapshot snapshot) =>
        snapshot.EarningEvaluationVersion is not null
        || snapshot.EarningRuleSetFingerprint is not null
        || snapshot.EarningRuleId.HasValue
        || snapshot.EarningRuleName is not null
        || snapshot.EarningRuleMinimumMinor.HasValue
        || snapshot.EarningRuleMaximumMinor.HasValue
        || snapshot.EarningRulePoints.HasValue
        || snapshot.EarningRulePriority.HasValue;

    private async Task<OrderBillingAwardWitness?> ReadAwardWitnessAsync(
        Guid orderId, CancellationToken cancellationToken)
    {
        var rows = await _context.OrderBillingAwardWitnesses.AsNoTracking()
            .Where(value => value.OrderId == orderId)
            .Take(2)
            .ToListAsync(cancellationToken);
        if (rows.Count > 1)
            throw new ConflictException("Duplicate loyalty award witnesses require reconciliation.");
        return rows.SingleOrDefault();
    }

    private async Task<List<FidelityPointsTransaction>> ReadEarnedRowsAsync(
        Guid orderId, CancellationToken cancellationToken) => await _context.FidelityPointsTransactions
        .AsNoTracking()
        .Where(value => value.OrderId == orderId && value.TransactionType == TransactionType.Earned)
        .OrderBy(value => value.Id)
        .Take(2)
        .ToListAsync(cancellationToken);

    private Task<bool> IsProviderManagedAsync(Guid orderId, CancellationToken cancellationToken) =>
        _context.ExternalOrderReferences.AsNoTracking()
            .AnyAsync(value => value.OrderId == orderId, cancellationToken);

    private static bool MatchesAward(
        FidelityPointsTransaction transaction, AwardOrderState order,
        Guid expectedUserId, int expectedPoints, decimal expectedOrderTotal) =>
        transaction.UserId == expectedUserId
        && transaction.OrderId == order.Id
        && transaction.TransactionType == TransactionType.Earned
        && transaction.Points == expectedPoints
        && transaction.OrderTotal == expectedOrderTotal;

    private sealed record AwardOrderState(
        Guid Id,
        Guid? UserId,
        OrderStatus Status,
        PaymentStatus PaymentStatus,
        int FidelityPointsEarned,
        decimal SubTotal,
        string OrderNumber);
}
