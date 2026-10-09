using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Features.AccountPayments.Dtos;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.AccountPayments.Services;

internal static partial class AccountCashRefundHistoryReader
{
    private static AccountCashRefundHistoryReadRequest PrepareRequest(
        IReadOnlyCollection<Guid> attemptIds, IReadOnlySet<Guid>? cancellationTargetLegIds)
    {
        AccountCashRefundHistoryReadLimit.RequireWithinLimit(attemptIds.Count);
        var targets = cancellationTargetLegIds?.ToHashSet();
        if (targets is not null && (targets.Count == 0 || targets.Contains(Guid.Empty)))
            throw ReconciliationRequired();
        if (targets is not null)
            AccountCashRefundHistoryReadLimit.RequireWithinLimit(targets.Count);
        return new AccountCashRefundHistoryReadRequest(attemptIds.Distinct().ToArray(), targets);
    }

    private static Dictionary<Guid, AccountCashRefundHistory> EmptyHistory(
        AccountCashRefundHistoryReadRequest request)
    {
        if (request.CancellationTargetLegIds is not null)
            throw ReconciliationRequired();
        return new Dictionary<Guid, AccountCashRefundHistory>();
    }

    private static Task<List<AccountPaymentAttempt>> ReadAttemptsAsync(
        ApplicationDbContext context, Guid[] attemptIds, CancellationToken cancellationToken) =>
        AccountCashRefundHistoryReadLimit.ReadAsync(context.AccountPaymentAttempts.AsNoTracking()
            .Where(value => attemptIds.Contains(value.Id))
            .Include(value => value.CashCollectionReceipt), cancellationToken);

    private static async Task<AccountCashRefundHistoryLegRows> ReadLegRowsAsync(
        ApplicationDbContext context, AccountPaymentAttempt[] cashAttempts,
        IReadOnlySet<Guid>? cancellationTargets, Guid? excludedOperationId,
        CancellationToken cancellationToken)
    {
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
        var excludedLegIds = GetExcludedLegIds(allLegs, excludedOperationId);
        var legs = allLegs.Where(value => !excludedLegIds.Contains(value.Id)).ToArray();
        var legIds = legs.Select(value => value.Id).ToArray();
        var legIdSet = legIds.ToHashSet();
        RequireTargetsExist(cancellationTargets, legIdSet);
        var targetsByAttempt = GroupTargetsByAttempt(legs, cancellationTargets);
        if (await context.OrderAmendmentRefundLegs.AsNoTracking()
                .AnyAsync(value => legIds.Contains(value.Id) && value.Attempts.Any(), cancellationToken))
            throw ReconciliationRequired();

        var intents = await AccountCashRefundHistoryReadLimit.ReadAsync(
            context.AccountCashRefundIntents.AsNoTracking()
                .Where(value => cashAttemptIds.Contains(value.AttemptId)
                    && (excludedOperationId == null || value.OperationId != excludedOperationId))
                .Include(value => value.ReturnEvidence), cancellationToken);
        if (intents.Any(value => !legIdSet.Contains(value.RefundLegId)))
            throw ReconciliationRequired();

        return new AccountCashRefundHistoryLegRows(
            legs, excludedLegIds, intents.ToArray(), targetsByAttempt);
    }

    private static void RequireTargetsExist(IReadOnlySet<Guid>? targets, HashSet<Guid> legIds)
    {
        if (targets is not null && targets.Any(value => !legIds.Contains(value)))
            throw ReconciliationRequired();
    }

    private static Dictionary<Guid, IReadOnlySet<Guid>>? GroupTargetsByAttempt(
        IReadOnlyList<OrderAmendmentRefundLeg> legs, IReadOnlySet<Guid>? targets)
    {
        if (targets is null)
            return null;
        return legs.Where(value => targets.Contains(value.Id))
            .GroupBy(value => value.AccountPaymentAttemptId ?? throw ReconciliationRequired())
            .ToDictionary(value => value.Key,
                value => (IReadOnlySet<Guid>)value.Select(leg => leg.Id).ToHashSet());
    }

    private static async Task<AccountCashRefundHistoryLedgerRows> ReadLedgerRowsAsync(
        ApplicationDbContext context, AccountPaymentAttempt[] cashAttempts,
        AccountCashRefundHistoryLegRows legRows, CancellationToken cancellationToken)
    {
        var allocationIds = cashAttempts.SelectMany(value => value.Allocations)
            .Select(value => value.Id).ToArray();
        var reversals = allocationIds.Length == 0 ? []
            : await AccountCashRefundHistoryReadLimit.ReadAsync(
                context.AccountPaymentAllocationReversals.AsNoTracking()
                    .Where(value => allocationIds.Contains(value.AllocationId)
                        && !legRows.ExcludedLegIds.Contains(value.RefundLegId)), cancellationToken);
        var legIds = legRows.Legs.Select(value => value.Id).ToArray();
        var evidence = legIds.Length == 0 ? []
            : await AccountCashRefundHistoryReadLimit.ReadAsync(
                context.OrderAmendmentRefundEvidence.AsNoTracking()
                    .Where(value => legIds.Contains(value.RefundLegId)), cancellationToken);
        var operationIds = legRows.Legs.Select(value => value.OperationId).Distinct().ToArray();
        var operations = operationIds.Length == 0 ? []
            : await AccountCashRefundHistoryReadLimit.ReadAsync(
                context.OrderAmendmentResolutionOperations.AsNoTracking()
                    .Where(value => operationIds.Contains(value.Id)), cancellationToken);
        return new AccountCashRefundHistoryLedgerRows(
            reversals.ToArray(), evidence.ToArray(), operations.ToArray());
    }

    private static Dictionary<Guid, AccountCashRefundHistory> ReadHistories(
        AccountPaymentAttempt[] cashAttempts, AccountCashRefundHistoryLegRows legRows,
        AccountCashRefundHistoryLedgerRows ledgerRows, CancellationToken cancellationToken)
    {
        var result = new Dictionary<Guid, AccountCashRefundHistory>();
        foreach (var attempt in cashAttempts)
        {
            var receipt = attempt.CashCollectionReceipt!;
            ValidateCapturedAttempt(attempt, receipt);
            var attemptAllocationIds = attempt.Allocations.Select(value => value.Id).ToHashSet();
            var evidence = new AccountCashHistoryEvidence(
                legRows.Legs.Where(value => value.AccountPaymentAttemptId == attempt.Id).ToArray(),
                legRows.Intents.Where(value => value.AttemptId == attempt.Id).ToArray(),
                ledgerRows.Reversals.Where(value => attemptAllocationIds.Contains(value.AllocationId)).ToArray(),
                ledgerRows.Evidence, ledgerRows.Operations);
            result.Add(attempt.Id, ReadOne(new AccountCashRefundHistoryWalkContext(
                attempt, receipt, evidence,
                legRows.TargetsByAttempt?.GetValueOrDefault(attempt.Id)), cancellationToken));
        }
        return result;
    }

    private static void ValidateCapturedAttempt(
        AccountPaymentAttempt attempt, AccountCashCollectionReceipt receipt)
    {
        var snapshot = AccountPaymentSnapshots.Deserialize<AccountPaymentQuoteSnapshot>(attempt.SnapshotJson);
        AccountCashCaptureReceiptPolicy.ValidateStored(attempt, snapshot);
        ValidateCapturedAllocations(attempt);
        var original = new CashSettlementQuote(receipt.PolicyVersion, receipt.Currency,
            receipt.PaymentMethod, receipt.ExactAmountMinor, receipt.AdjustmentMinor,
            receipt.DueAmountMinor);
        AccountCashSettlementPolicy.RequireMatches(original, attempt.Currency,
            attempt.PaymentMethod, attempt.AmountMinor);
    }
}
