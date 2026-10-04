using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.AccountPayments.Dtos;
using RestaurantSystem.Api.Features.OrderAmendments.Dtos;
using RestaurantSystem.Api.Features.OrderAmendments.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.AccountPayments.Services;

internal static class AccountCashRefundHistoryReader
{
    internal static async Task<IReadOnlyDictionary<Guid, AccountCashRefundHistory>> ReadAsync(
        ApplicationDbContext context, IReadOnlyCollection<Guid> attemptIds,
        CancellationToken cancellationToken, Guid? excludedOperationId = null)
    {
        AccountCashRefundHistoryReadLimit.RequireWithinLimit(attemptIds.Count);
        var ids = attemptIds.Distinct().ToArray();
        if (ids.Length == 0)
            return new Dictionary<Guid, AccountCashRefundHistory>();

        var attempts = await AccountCashRefundHistoryReadLimit.ReadAsync(
            context.AccountPaymentAttempts.AsNoTracking()
            .Where(value => ids.Contains(value.Id))
            .Include(value => value.CashCollectionReceipt), cancellationToken);
        var cashAttempts = attempts.Where(value => value.PaymentMethod == PaymentMethod.Cash
                && value.CashCollectionReceipt is not null)
            .ToArray();
        if (cashAttempts.Length == 0)
            return new Dictionary<Guid, AccountCashRefundHistory>();

        var cashAttemptIds = cashAttempts.Select(value => value.Id).ToArray();
        var allocations = await AccountCashRefundHistoryReadLimit.ReadAsync(
            context.AccountPaymentAllocations.AsNoTracking()
                .Where(value => cashAttemptIds.Contains(value.AttemptId)), cancellationToken);
        foreach (var attempt in cashAttempts)
            attempt.Allocations = allocations.Where(value => value.AttemptId == attempt.Id).ToArray();
        var allLegs = await AccountCashRefundHistoryReadLimit.ReadAsync(
            context.OrderAmendmentRefundLegs.AsNoTracking()
            .Where(value => value.AccountPaymentAttemptId.HasValue
                && cashAttemptIds.Contains(value.AccountPaymentAttemptId.Value))
            .Include(value => value.CashRefundIntent!.ReturnEvidence), cancellationToken);
        HashSet<Guid> excludedLegIds = excludedOperationId is Guid excludedId
            ? allLegs.Where(value => value.OperationId == excludedId).Select(value => value.Id).ToHashSet()
            : [];
        var legs = allLegs.Where(value => !excludedLegIds.Contains(value.Id)).ToArray();
        var legIds = legs.Select(value => value.Id).ToArray();
        if (await context.OrderAmendmentRefundLegs.AsNoTracking()
                .AnyAsync(value => legIds.Contains(value.Id) && value.Attempts.Any(), cancellationToken))
            throw ReconciliationRequired();
        var intents = await AccountCashRefundHistoryReadLimit.ReadAsync(
            context.AccountCashRefundIntents.AsNoTracking()
            .Where(value => cashAttemptIds.Contains(value.AttemptId)
                && (excludedOperationId == null || value.OperationId != excludedOperationId))
            .Include(value => value.ReturnEvidence), cancellationToken);
        if (intents.Any(value => !legIds.Contains(value.RefundLegId)))
            throw ReconciliationRequired();

        var allocationIds = cashAttempts.SelectMany(value => value.Allocations)
            .Select(value => value.Id).ToArray();
        var reversals = allocationIds.Length == 0 ? []
            : await AccountCashRefundHistoryReadLimit.ReadAsync(
                context.AccountPaymentAllocationReversals.AsNoTracking()
                .Where(value => allocationIds.Contains(value.AllocationId)
                    && !excludedLegIds.Contains(value.RefundLegId)), cancellationToken);
        var evidence = legIds.Length == 0 ? []
            : await AccountCashRefundHistoryReadLimit.ReadAsync(
                context.OrderAmendmentRefundEvidence.AsNoTracking()
                .Where(value => legIds.Contains(value.RefundLegId)), cancellationToken);
        var operationIds = legs.Select(value => value.OperationId).Distinct().ToArray();
        var operations = operationIds.Length == 0 ? []
            : await AccountCashRefundHistoryReadLimit.ReadAsync(
                context.OrderAmendmentResolutionOperations.AsNoTracking()
                .Where(value => operationIds.Contains(value.Id)), cancellationToken);

        var result = new Dictionary<Guid, AccountCashRefundHistory>();
        foreach (var attempt in cashAttempts)
        {
            var receipt = attempt.CashCollectionReceipt!;
            var snapshot = AccountPaymentSnapshots.Deserialize<AccountPaymentQuoteSnapshot>(attempt.SnapshotJson);
            AccountCashCaptureReceiptPolicy.ValidateStored(attempt, snapshot);
            ValidateCapturedAllocations(attempt);
            var original = new CashSettlementQuote(receipt.PolicyVersion, receipt.Currency,
                receipt.PaymentMethod, receipt.ExactAmountMinor, receipt.AdjustmentMinor,
                receipt.DueAmountMinor);
            AccountCashSettlementPolicy.RequireMatches(original, attempt.Currency,
                attempt.PaymentMethod, attempt.AmountMinor);
            var attemptAllocationIds = attempt.Allocations.Select(value => value.Id).ToHashSet();

            result.Add(attempt.Id, ReadOne(attempt, receipt, new AccountCashHistoryEvidence(
                legs.Where(value => value.AccountPaymentAttemptId == attempt.Id).ToArray(),
                intents.Where(value => value.AttemptId == attempt.Id).ToArray(),
                reversals.Where(value => attemptAllocationIds.Contains(value.AllocationId)).ToArray(),
                evidence, operations), cancellationToken));
        }
        return result;
    }

    private static AccountCashRefundHistory ReadOne(
        AccountPaymentAttempt attempt, AccountCashCollectionReceipt receipt,
        AccountCashHistoryEvidence historyEvidence, CancellationToken cancellationToken)
    {
        var (legs, intents, reversals, evidence, operations) = historyEvidence;
        cancellationToken.ThrowIfCancellationRequested();
        if (legs.Length != intents.Length || legs.Any(value => value.CashRefundIntent is null)
            || intents.Any(value => legs.All(leg => leg.Id != value.RefundLegId)))
            throw ReconciliationRequired();

        var byLeg = legs.ToDictionary(value => value.Id);
        var operationById = operations.ToDictionary(value => value.Id);
        var allocationById = attempt.Allocations.ToDictionary(value => value.Id);
        var remaining = intents.ToList();
        var refundedExact = 0L;
        var refundedCash = 0L;
        var fingerprint = AccountCashRefundHistoryFingerprint.Seed(receipt);
        while (remaining.Count > 0)
        {
            var next = remaining.Where(value => value.PriorHistoryFingerprint == fingerprint
                && value.PreviouslyRefundedExactMinor == refundedExact
                && value.PreviouslyRefundedCashMinor == refundedCash).ToArray();
            if (next.Length != 1)
                throw ReconciliationRequired();
            var intent = next[0];
            var leg = byLeg[intent.RefundLegId];
            if (!operationById.TryGetValue(intent.OperationId, out var operation))
                throw ReconciliationRequired();
            var scopes = OrderAmendmentJson.Deserialize<List<OrderAmendmentRefundScope>>(leg.FrozenScopesJson);
            var legEvidence = evidence.Where(value => value.RefundLegId == leg.Id).ToArray();
            var legReversals = reversals.Where(value => value.RefundLegId == leg.Id).ToArray();
            var returnEvidence = intent.ReturnEvidence;
            ValidateRefund(intent, new AccountCashRefundProof(leg, operation, receipt, scopes,
                legEvidence, legReversals, allocationById), returnEvidence,
                new AccountCashRefundHistory(refundedExact, refundedCash, fingerprint));

            refundedExact = checked(refundedExact + intent.ExactRefundAmountMinor);
            refundedCash = checked(refundedCash + intent.CashRefundAmountMinor);
            fingerprint = AccountCashRefundHistoryFingerprint.Advance(
                fingerprint, intent, returnEvidence!, scopes, legReversals);
            remaining.Remove(intent);
        }

        var allLegIds = legs.Select(value => value.Id).ToHashSet();
        if (reversals.Any(value => !allLegIds.Contains(value.RefundLegId))
            || reversals.Sum(value => value.AmountMinor) != refundedExact)
            throw ReconciliationRequired();
        return new AccountCashRefundHistory(refundedExact, refundedCash, fingerprint);
    }

    private static void ValidateCapturedAllocations(AccountPaymentAttempt attempt)
    {
        if (attempt.State != AccountPaymentState.Captured || attempt.Allocations.Count == 0
            || attempt.Allocations.Any(value => value.AmountMinor != checked(value.MinorPerUnit * value.UnitCount)
                || value.OrderPaymentId is null)
            || attempt.Allocations.Sum(value => value.AmountMinor) != attempt.AmountMinor)
            throw ReconciliationRequired();
    }

    private static void ValidateRefund(
        AccountCashRefundIntent intent, AccountCashRefundProof proof,
        AccountCashRefundEvidence? returned, AccountCashRefundHistory history)
    {
        var (leg, operation, receipt, scopes, evidence, reversals, allocations) = proof;
        var expected = AccountCashRefundIntentValidator.RequireMatchesHistory(intent, leg,
            operation, receipt, history);
        if (intent.RefundLegId != leg.Id || intent.OperationId != leg.OperationId
            || intent.AttemptId != receipt.AttemptId || intent.CollectionReceiptId != receipt.Id
            || intent.PolicyVersion != expected.PolicyVersion || intent.Currency != expected.Currency
            || intent.OriginalExactAmountMinor != receipt.ExactAmountMinor
            || intent.OriginalAdjustmentMinor != receipt.AdjustmentMinor
            || intent.OriginalDueAmountMinor != receipt.DueAmountMinor
            || intent.ExactRefundAmountMinor != leg.AmountMinor
            || intent.RefundAdjustmentMinor != expected.RefundAdjustmentMinor
            || intent.CashRefundAmountMinor != expected.CashRefundAmountMinor
            || intent.RetainedExactAmountMinor != expected.RetainedExactAmountMinor
            || intent.RetainedCashDueMinor != expected.RetainedCashDueMinor
            || leg.Custody != OrderAmendmentRefundCustody.ManualTill
            || leg.AccountPaymentAttemptId != receipt.AttemptId
            || leg.State != OrderAmendmentRefundLegState.Succeeded || leg.ResolvedAt is null
            || operation.Id != intent.OperationId || operation.State != OrderAmendmentResolutionOperationState.Resolved
            || operation.ResolvedAt is null || operation.Currency != receipt.Currency
            || operation.ActorUserId == Guid.Empty || operation.ActorRole != UserRole.Admin.ToString()
            || leg.Attempts.Count != 0 || !AccountAmendmentRefundIntegrity.HasNoProviderContext(leg)
            || !OrderAmendmentTillReferencePolicy.IsValid(leg.ManualTillReference)
            || evidence.Length != 1 || evidence[0].Kind != OrderAmendmentRefundEvidenceKind.ManualTillConfirmation
            || evidence[0].State != OrderAmendmentRefundLegState.Succeeded
            || evidence[0].AmountMinor != leg.AmountMinor || evidence[0].Currency != leg.Currency
            || evidence[0].TillReference != leg.ManualTillReference
            || evidence[0].ActorUserId != operation.ActorUserId
            || evidence[0].ActorRole != operation.ActorRole || evidence[0].ObservedAt != leg.ResolvedAt
            || scopes.Count == 0
            || scopes.Sum(value => value.AmountMinor) != leg.AmountMinor
            || reversals.Length != scopes.Count)
            throw ReconciliationRequired();

        AccountCashRefundIntentValidator.RequireReturnEvidence(intent, leg, operation, expected, returned);
        ValidateSavedResult(operation, leg, intent, returned!);

        foreach (var scope in scopes)
        {
            if (!allocations.TryGetValue(scope.AllocationId, out var allocation)
                || allocation.AttemptId != receipt.AttemptId || allocation.OrderPaymentId != leg.SourcePaymentId
                || allocation.OrderId != scope.OrderId || allocation.OrderItemId != scope.OrderItemId
                || allocation.MinorPerUnit != scope.MinorPerUnit || scope.UnitCount <= 0
                || scope.StartOrdinal < allocation.StartOrdinal
                || (long)scope.StartOrdinal + scope.UnitCount > (long)allocation.StartOrdinal + allocation.UnitCount
                || scope.AmountMinor != checked(scope.UnitCount * scope.MinorPerUnit))
                throw ReconciliationRequired();
            var reversal = reversals.SingleOrDefault(value => value.AllocationId == scope.AllocationId
                && value.StartOrdinal == scope.StartOrdinal && value.UnitCount == scope.UnitCount);
            if (reversal is null || reversal.RefundLegId != leg.Id || reversal.OrderId != scope.OrderId
                || reversal.OrderItemId != scope.OrderItemId || reversal.MinorPerUnit != scope.MinorPerUnit
                || reversal.AmountMinor != scope.AmountMinor || reversal.Currency != receipt.Currency
                || reversal.ActorUserId != operation.ActorUserId || reversal.ActorRole != operation.ActorRole)
                throw ReconciliationRequired();
        }
    }

    private static void ValidateSavedResult(
        OrderAmendmentResolutionOperation operation, OrderAmendmentRefundLeg leg,
        AccountCashRefundIntent intent, AccountCashRefundEvidence returned)
    {
        if (operation.ResultJson is null)
            throw ReconciliationRequired();
        var result = OrderAmendmentJson.Deserialize<OrderAmendmentResolutionResultDto>(operation.ResultJson);
        var matching = result.RefundLegs.Where(value => value.PaymentId == leg.SourcePaymentId).ToArray();
        if (matching.Length != 1
            || matching[0].CashRefund != OrderAmendmentCashRefundMapper.Map(intent)
            || matching[0].CashReturn is not { } cashReturn
            || !PostgresTimestampPrecision.MatchesColumn(cashReturn.ConfirmedAt, returned.ObservedAt)
            || cashReturn with { ConfirmedAt = returned.ObservedAt }
                != OrderAmendmentCashRefundMapper.Map(returned))
            throw ReconciliationRequired();
    }

    private static ConflictException ReconciliationRequired() =>
        new("The original cash receipt has unresolved or inconsistent refund history.");
}
