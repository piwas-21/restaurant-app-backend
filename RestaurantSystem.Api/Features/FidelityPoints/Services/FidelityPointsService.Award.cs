using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.FidelityPoints.Models;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.FidelityPoints.Services;

public partial class FidelityPointsService
{
    private const string AwardJournalAuditIdentifier = "OrderBillingAwardBoundary";

    private sealed record BackfillAwardInput(
        AwardOrderState Order, FidelityPointsTransaction Existing, OrderBillingSnapshotOwnerLink OwnerLink,
        Guid UserId, int Candidate, int Applied, int Suppressed, decimal EarningTotal,
        IReadOnlyList<OrderBillingSnapshotUnit> Units);

    private sealed record RecordAwardInput(
        AwardOrderState Order, OrderBillingSnapshotOwnerLink OwnerLink, Guid UserId,
        int Candidate, int Applied, int Suppressed, decimal EarningTotal,
        IReadOnlyList<OrderBillingSnapshotUnit> Units,
        IReadOnlyList<OrderBillingUnitAwardSuppression> Suppressions);

    public async Task<FidelityPointsAwardResult> AwardAcceptedOrderAsync(
        Guid orderId, CancellationToken cancellationToken = default)
    {
        var transaction = _context.Database.CurrentTransaction is null
            ? await _context.Database.BeginTransactionAsync(cancellationToken)
            : null;
        try
        {
            return await AwardLockedOrderAsync(orderId, transaction, cancellationToken);
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

    private async Task<FidelityPointsAwardResult> AwardLockedOrderAsync(
        Guid orderId, Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction? transaction,
        CancellationToken cancellationToken)
    {
        var order = await LockAwardOrderAsync(orderId, cancellationToken);
        if (order is null)
            return Deferred(FidelityPointsAwardDeferralReason.OrderUnavailable);

        var witness = await ReadAwardWitnessAsync(orderId, cancellationToken);
        if (witness is null)
            return await AwardWithoutWitnessAsync(order, transaction, cancellationToken);

        var replay = await ReplayWitnessAsync(witness, order, cancellationToken);
        await CommitOwnedTransactionAsync(transaction, cancellationToken);
        return replay;
    }

    private async Task<FidelityPointsAwardResult> AwardWithoutWitnessAsync(
        AwardOrderState order, Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction? transaction,
        CancellationToken cancellationToken)
    {
        var snapshot = await _context.OrderBillingSnapshots.AsNoTracking()
            .SingleOrDefaultAsync(value => value.OrderId == order.Id, cancellationToken);
        var existingAwards = await ReadEarnedRowsAsync(order.Id, cancellationToken);
        if (existingAwards.Count > 1)
            throw new ConflictException("Duplicate loyalty awards require reconciliation.");
        if (snapshot is null)
            return await ReplayLegacyWithoutSnapshotAsync(order, existingAwards, transaction, cancellationToken);
        if (!snapshot.EarnedPointsCandidate.HasValue)
            return await AwardWithoutAcceptedCandidateAsync(
                order, snapshot, existingAwards, transaction, cancellationToken);

        return await AwardFromSnapshotAsync(order, snapshot, existingAwards, transaction, cancellationToken);
    }

    private async Task<FidelityPointsAwardResult> AwardFromSnapshotAsync(
        AwardOrderState order, OrderBillingSnapshot snapshot,
        List<FidelityPointsTransaction> existingAwards,
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction? transaction,
        CancellationToken cancellationToken)
    {
        var candidate = ValidateSnapshotCandidate(order, snapshot);
        var ownerLink = await LockEarningOwnerLinkAsync(order.Id, cancellationToken);
        if (!IsLinkedOwner(ownerLink, order.UserId)
            || !await TryLockAwardUserAsync(order.UserId!.Value, cancellationToken))
            return Deferred(FidelityPointsAwardDeferralReason.EarningOwnerUnavailable);
        var userId = ownerLink!.UserId!.Value;
        if (existingAwards.Count == 0)
        {
            var ineligible = ReadIneligibleReason(order);
            if (ineligible.HasValue)
                return Deferred(ineligible.Value);
            if (await IsProviderManagedAsync(order.Id, cancellationToken))
                return Deferred(FidelityPointsAwardDeferralReason.ProviderManaged);
        }

        var units = await ReadSnapshotUnitsAsync(order.Id, cancellationToken);
        ValidateUnitAllocation(candidate, units);
        var suppressions = await ReadUnitSuppressionsAsync(order.Id, cancellationToken);
        var suppressed = await ValidateSuppressionsAsync(order.Id, units, suppressions, _context,
            AwardSuppressionValidationMode.CompletePreAwardCoverage, cancellationToken);
        if (suppressed > candidate)
            throw new ConflictException("The loyalty removal history exceeds the frozen earning candidate.");
        var applied = checked(candidate - suppressed);
        var earningTotal = SnapshotEarningTotal(snapshot);

        if (existingAwards.Count == 1)
            return await BackfillExistingAwardAsync(new(order, existingAwards[0], ownerLink,
                userId, candidate, applied, suppressed, earningTotal, units), transaction, cancellationToken);
        if (applied == 0)
            return await RecordNoAwardAsync(order.Id, ownerLink, candidate, suppressed, transaction, cancellationToken);
        return await RecordAwardAsync(new(order, ownerLink, userId, candidate, applied,
            suppressed, earningTotal, units, suppressions), transaction, cancellationToken);
    }

    private async Task<FidelityPointsAwardResult> BackfillExistingAwardAsync(
        BackfillAwardInput input,
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction? transaction,
        CancellationToken cancellationToken)
    {
        if (input.Suppressed != 0 || input.Applied != input.Candidate
            || !MatchesAward(input.Existing, input.Order, input.UserId,
                input.Candidate, input.EarningTotal))
            throw new ConflictException("The original loyalty award does not match the frozen snapshot.");
        if (await HasCommittedRemovalAmendmentAsync(input.Order.Id, cancellationToken))
            throw new ConflictException("A legacy award with accepted removals has no provable per-unit award coverage.");
        if (await LockBalanceRowForLoyaltyAsync(input.UserId, cancellationToken) is null)
            throw new ConflictException("The original loyalty award has no balance record.");

        var backfilled = NewWitness(input.Order.Id, input.OwnerLink.Id,
            input.Candidate, input.Candidate, 0, OrderBillingAwardOutcome.Awarded, input.Existing.Id);
        _context.OrderBillingAwardWitnesses.Add(backfilled);
        AddAwardUnitCoverage(input.Order.Id, backfilled.Id, input.Units);
        await _context.SaveChangesAsync(cancellationToken);
        await CommitOwnedTransactionAsync(transaction, cancellationToken);
        return Result(FidelityPointsAwardDisposition.AlreadyAwarded,
            input.Candidate, input.Candidate, 0);
    }

    private async Task<FidelityPointsAwardResult> RecordNoAwardAsync(
        Guid orderId, OrderBillingSnapshotOwnerLink ownerLink, int candidate, int suppressed,
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction? transaction,
        CancellationToken cancellationToken)
    {
        var outcome = candidate == 0
            ? OrderBillingAwardOutcome.EvaluatedZero
            : OrderBillingAwardOutcome.FullySuppressed;
        _context.OrderBillingAwardWitnesses.Add(NewWitness(
            orderId, ownerLink.Id, candidate, 0, suppressed, outcome, null));
        await _context.SaveChangesAsync(cancellationToken);
        await CommitOwnedTransactionAsync(transaction, cancellationToken);
        var disposition = candidate == 0
            ? FidelityPointsAwardDisposition.EvaluatedZero
            : FidelityPointsAwardDisposition.FullySuppressed;
        return Result(disposition, candidate, 0, suppressed);
    }

    private async Task<FidelityPointsAwardResult> RecordAwardAsync(
        RecordAwardInput input,
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction? transaction,
        CancellationToken cancellationToken)
    {
        var balance = await LockBalanceRowForLoyaltyAsync(input.UserId, cancellationToken);
        var now = DateTime.UtcNow;
        var auditIdentifier = _currentUserService.GetAuditIdentifier();
        ApplyAwardBalance(balance, input.UserId, input.Applied, now, auditIdentifier);
        var earned = NewEarnedTransaction(input.Order, input.UserId, input.Applied,
            input.EarningTotal, now, auditIdentifier);
        _context.FidelityPointsTransactions.Add(earned);
        var witness = NewWitness(input.Order.Id, input.OwnerLink.Id, input.Candidate,
            input.Applied, input.Suppressed,
            OrderBillingAwardOutcome.Awarded, earned.Id);
        _context.OrderBillingAwardWitnesses.Add(witness);
        AddAwardUnitCoverage(input.Order.Id, witness.Id, input.Units, input.Suppressions);
        await _context.SaveChangesAsync(cancellationToken);
        await CommitOwnedTransactionAsync(transaction, cancellationToken);
        return Result(FidelityPointsAwardDisposition.Awarded,
            input.Candidate, input.Applied, input.Suppressed);
    }

    private static Task CommitOwnedTransactionAsync(
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction? transaction,
        CancellationToken cancellationToken) => transaction is null
        ? Task.CompletedTask : transaction.CommitAsync(cancellationToken);

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
