using RestaurantSystem.Api.Features.OrderAmendments.Dtos;
using RestaurantSystem.Api.Features.OrderAmendments.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.AccountPayments.Services;

internal static partial class AccountCashRefundHistoryReader
{
    private static AccountCashRefundHistory ReadHistorySequence(
        AccountCashRefundHistoryWalkContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var evidence = context.Evidence;
        if (evidence.Legs.Length != evidence.Intents.Length
            || evidence.Legs.Any(value => value.CashRefundIntent is null)
            || evidence.Intents.Any(value => evidence.Legs.All(leg => leg.Id != value.RefundLegId)))
            throw ReconciliationRequired();

        var byLeg = evidence.Legs.ToDictionary(value => value.Id);
        var operationById = evidence.Operations.ToDictionary(value => value.Id);
        var targetScope = AccountCashRefundHistoryPrefix.TargetScope(
            context.CancellationTargetLegIds, byLeg, operationById);
        var walk = new HistoryWalk(context, byLeg, operationById, targetScope);
        while (walk.HasRemaining)
        {
            var intent = walk.TakeNextIntent();
            var leg = walk.LegFor(intent);
            if (walk.IsPendingTail(leg))
            {
                ValidatePendingTail(walk, intent, leg);
                break;
            }
            ApplyResolvedRefund(walk, intent, leg);
        }

        walk.RequireAllTargetsValidated();
        walk.RequireReversalsMatchHistory();
        return walk.History;
    }

    private static void ValidatePendingTail(
        HistoryWalk walk, AccountCashRefundIntent intent, OrderAmendmentRefundLeg leg)
    {
        if (walk.RemainingCount != 1 || !walk.OperationById.TryGetValue(intent.OperationId, out var operation))
            throw ReconciliationRequired();

        AccountCashRefundHistoryPrefix.RequireUnrelatedPendingTail(
            intent, leg, operation, new AccountCashRefundPendingTailProof(
                walk.Context.Receipt, walk.Context.Attempt.ServiceSessionId, walk.History,
                walk.Evidence.Where(value => value.RefundLegId == leg.Id).ToArray(),
                walk.Reversals.Where(value => value.RefundLegId == leg.Id).ToArray(),
                new AccountCashRefundPendingTailScope(walk.Reversals, walk.AllocationById,
                    walk.PriorOperationIds, walk.TargetSourceOrderIds)));
    }

    private static void ApplyResolvedRefund(
        HistoryWalk walk, AccountCashRefundIntent intent, OrderAmendmentRefundLeg leg)
    {
        if (!walk.OperationById.TryGetValue(intent.OperationId, out var operation))
            throw ReconciliationRequired();
        var scopes = OrderAmendmentJson.Deserialize<List<OrderAmendmentRefundScope>>(leg.FrozenScopesJson);
        var evidence = walk.Evidence.Where(value => value.RefundLegId == leg.Id).ToArray();
        var reversals = walk.Reversals.Where(value => value.RefundLegId == leg.Id).ToArray();
        var returned = intent.ReturnEvidence;
        ValidateRefund(intent, new AccountCashRefundProof(leg, operation, walk.Context.Receipt,
            scopes, evidence, reversals, walk.AllocationById), returned, walk.History);
        walk.Advance(intent, operation, leg, returned!, scopes, reversals);
    }

    private sealed class HistoryWalk
    {
        private readonly Dictionary<Guid, OrderAmendmentRefundLeg> legsById;
        private readonly IReadOnlySet<Guid>? targetLegIds;
        private readonly HashSet<Guid> validatedTargetLegIds = [];
        private readonly List<AccountCashRefundIntent> remaining;

        internal HistoryWalk(
            AccountCashRefundHistoryWalkContext context,
            Dictionary<Guid, OrderAmendmentRefundLeg> legsById,
            Dictionary<Guid, OrderAmendmentResolutionOperation> operationById,
            (HashSet<Guid> OperationIds, HashSet<Guid> SourceOrderIds) targetScope)
        {
            Context = context;
            this.legsById = legsById;
            OperationById = operationById;
            targetLegIds = context.CancellationTargetLegIds;
            remaining = context.Evidence.Intents.ToList();
            Reversals = context.Evidence.Reversals;
            Evidence = context.Evidence.Evidence;
            AllocationById = context.Attempt.Allocations.ToDictionary(value => value.Id);
            PriorOperationIds = targetScope.OperationIds;
            TargetSourceOrderIds = targetScope.SourceOrderIds;
            History = new AccountCashRefundHistory(0, 0,
                AccountCashRefundHistoryFingerprint.Seed(context.Receipt));
        }

        internal AccountCashRefundHistoryWalkContext Context { get; }
        internal Dictionary<Guid, OrderAmendmentResolutionOperation> OperationById { get; }
        internal IReadOnlyList<AccountPaymentAllocationReversal> Reversals { get; }
        internal IReadOnlyList<OrderAmendmentRefundEvidence> Evidence { get; }
        internal Dictionary<Guid, AccountPaymentAllocation> AllocationById { get; }
        internal HashSet<Guid> PriorOperationIds { get; }
        internal HashSet<Guid> TargetSourceOrderIds { get; }
        internal AccountCashRefundHistory History { get; private set; }
        internal bool HasRemaining => remaining.Count > 0;
        internal int RemainingCount => remaining.Count;

        internal AccountCashRefundIntent TakeNextIntent()
        {
            var next = remaining.Where(value => value.PriorHistoryFingerprint == History.Fingerprint
                && value.PreviouslyRefundedExactMinor == History.RefundedExactMinor
                && value.PreviouslyRefundedCashMinor == History.RefundedCashMinor).ToArray();
            return next.Length == 1 ? next[0] : throw ReconciliationRequired();
        }

        internal OrderAmendmentRefundLeg LegFor(AccountCashRefundIntent intent) => legsById[intent.RefundLegId];

        internal bool IsPendingTail(OrderAmendmentRefundLeg leg) =>
            targetLegIds is not null && validatedTargetLegIds.Count == targetLegIds.Count
            && leg.State == OrderAmendmentRefundLegState.Pending;

        internal void Advance(
            AccountCashRefundIntent intent, OrderAmendmentResolutionOperation operation,
            OrderAmendmentRefundLeg leg, AccountCashRefundEvidence returned,
            List<OrderAmendmentRefundScope> scopes,
            AccountPaymentAllocationReversal[] reversals)
        {
            PriorOperationIds.Add(operation.Id);
            var nextHistory = new AccountCashRefundHistory(
                checked(History.RefundedExactMinor + intent.ExactRefundAmountMinor),
                checked(History.RefundedCashMinor + intent.CashRefundAmountMinor),
                AccountCashRefundHistoryFingerprint.Advance(
                    History.Fingerprint, intent, returned, scopes, reversals));
            History = nextHistory;
            if (targetLegIds?.Contains(leg.Id) == true)
                validatedTargetLegIds.Add(leg.Id);
            remaining.Remove(intent);
        }

        internal void RequireAllTargetsValidated()
        {
            if (targetLegIds is not null && validatedTargetLegIds.Count != targetLegIds.Count)
                throw ReconciliationRequired();
        }

        internal void RequireReversalsMatchHistory()
        {
            var legIds = legsById.Keys.ToHashSet();
            if (Reversals.Any(value => !legIds.Contains(value.RefundLegId))
                || Reversals.Sum(value => value.AmountMinor) != History.RefundedExactMinor)
                throw ReconciliationRequired();
        }
    }
}
