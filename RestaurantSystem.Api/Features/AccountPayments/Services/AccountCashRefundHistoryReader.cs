using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.AccountPayments.Dtos;
using RestaurantSystem.Api.Features.OrderAmendments.Dtos;
using RestaurantSystem.Api.Features.OrderAmendments.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.AccountPayments.Services;

internal static partial class AccountCashRefundHistoryReader
{
    internal static async Task<IReadOnlyDictionary<Guid, AccountCashRefundHistory>> ReadAsync(
        ApplicationDbContext context, IReadOnlyCollection<Guid> attemptIds,
        CancellationToken cancellationToken, Guid? excludedOperationId = null,
        // Used only by cancellation integrity; null preserves whole-history validation.
        IReadOnlySet<Guid>? cancellationTargetLegIds = null)
    {
        var request = PrepareRequest(attemptIds, cancellationTargetLegIds);
        if (request.AttemptIds.Length == 0)
            return EmptyHistory(request);

        var attempts = await ReadAttemptsAsync(context, request.AttemptIds, cancellationToken);
        var cashAttempts = attempts.Where(value => value.PaymentMethod == PaymentMethod.Cash
                && value.CashCollectionReceipt is not null)
            .ToArray();
        if (cashAttempts.Length == 0)
            return EmptyHistory(request);

        var legRows = await ReadLegRowsAsync(context, cashAttempts, request.CancellationTargetLegIds,
            excludedOperationId, cancellationToken);
        var ledgerRows = await ReadLedgerRowsAsync(context, cashAttempts, legRows, cancellationToken);
        return ReadHistories(cashAttempts, legRows, ledgerRows, cancellationToken);
    }

    private static HashSet<Guid> GetExcludedLegIds(
        IReadOnlyCollection<OrderAmendmentRefundLeg> legs, Guid? excludedOperationId) =>
        excludedOperationId is Guid excludedId
            ? legs.Where(value => value.OperationId == excludedId).Select(value => value.Id).ToHashSet()
            : [];

    private static AccountCashRefundHistory ReadOne(
        AccountCashRefundHistoryWalkContext context, CancellationToken cancellationToken) =>
        ReadHistorySequence(context, cancellationToken);

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
