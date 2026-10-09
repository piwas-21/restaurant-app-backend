using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.OrderAmendments.Dtos;
using RestaurantSystem.Api.Features.OrderAmendments.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.AccountPayments.Services;

/// <summary>Allows cancellation checks to stop after their resolved cash legs, never before them.</summary>
internal static class AccountCashRefundHistoryPrefix
{
    internal static (HashSet<Guid> OperationIds, HashSet<Guid> SourceOrderIds) TargetScope(
        IReadOnlySet<Guid>? targetLegIds,
        IReadOnlyDictionary<Guid, OrderAmendmentRefundLeg> legs,
        IReadOnlyDictionary<Guid, OrderAmendmentResolutionOperation> operations)
    {
        var operationIds = new HashSet<Guid>();
        var sourceOrderIds = new HashSet<Guid>();
        if (targetLegIds is null)
            return (operationIds, sourceOrderIds);

        foreach (var legId in targetLegIds)
        {
            if (!legs.TryGetValue(legId, out var leg)
                || !operations.TryGetValue(leg.OperationId, out var operation)
                || operation.SourceOrderId == Guid.Empty)
                throw ReconciliationRequired();
            operationIds.Add(operation.Id);
            sourceOrderIds.Add(operation.SourceOrderId);
        }
        return (operationIds, sourceOrderIds);
    }

    internal static void RequireUnrelatedPendingTail(
        AccountCashRefundIntent intent, OrderAmendmentRefundLeg leg,
        OrderAmendmentResolutionOperation operation, AccountCashCollectionReceipt receipt,
        Guid attemptServiceSessionId,
        AccountCashRefundHistory history, IReadOnlyList<OrderAmendmentRefundEvidence> evidence,
        IReadOnlyList<AccountPaymentAllocationReversal> tailReversals,
        IReadOnlyList<AccountPaymentAllocationReversal> historyReversals,
        IReadOnlyDictionary<Guid, AccountPaymentAllocation> allocations,
        IReadOnlySet<Guid> priorOperationIds, IReadOnlySet<Guid> targetSourceOrderIds)
    {
        if (operation.Id != intent.OperationId || priorOperationIds.Contains(operation.Id)
            || operation.SourceOrderId == Guid.Empty || targetSourceOrderIds.Contains(operation.SourceOrderId)
            || operation.ServiceSessionId != attemptServiceSessionId
            || operation.State != OrderAmendmentResolutionOperationState.Processing
            || operation.ResolvedAt is not null || operation.FailureCode is not null
            || operation.ResultJson is not null
            || leg.State != OrderAmendmentRefundLegState.Pending || leg.ResolvedAt is not null
            || leg.FailureCode is not null || leg.ManualTillReference is not null
            || leg.Custody != OrderAmendmentRefundCustody.ManualTill
            || !AccountAmendmentRefundIntegrity.HasNoProviderContext(leg)
            || intent.ReturnEvidence is not null || evidence.Count != 0 || tailReversals.Count != 0)
            throw ReconciliationRequired();

        AccountCashRefundIntentValidator.RequireMatchesHistory(intent, leg, operation, receipt, history);
        var scopes = OrderAmendmentJson.Deserialize<List<OrderAmendmentRefundScope>>(leg.FrozenScopesJson);
        if (scopes.Count == 0 || scopes.Sum(value => value.AmountMinor) != leg.AmountMinor)
            throw ReconciliationRequired();

        for (var index = 0; index < scopes.Count; index++)
        {
            var scope = scopes[index];
            if (!allocations.TryGetValue(scope.AllocationId, out var allocation)
                || scope.OrderId != operation.SourceOrderId
                || allocation.AttemptId != receipt.AttemptId || allocation.OrderPaymentId != leg.SourcePaymentId
                || allocation.OrderId != scope.OrderId || allocation.OrderItemId != scope.OrderItemId
                || allocation.MinorPerUnit != scope.MinorPerUnit || scope.MinorPerUnit <= 0
                || scope.AmountMinor <= 0 || scope.UnitCount <= 0
                || scope.StartOrdinal < allocation.StartOrdinal
                || (long)scope.StartOrdinal + scope.UnitCount > (long)allocation.StartOrdinal + allocation.UnitCount
                || scope.AmountMinor != checked(scope.UnitCount * scope.MinorPerUnit))
                throw ReconciliationRequired();

            if (scopes.Take(index).Any(previous => previous.AllocationId == scope.AllocationId
                    && Overlaps(previous.StartOrdinal, previous.UnitCount, scope.StartOrdinal, scope.UnitCount))
                || historyReversals.Any(value => value.AllocationId == scope.AllocationId
                    && Overlaps(value.StartOrdinal, value.UnitCount, scope.StartOrdinal, scope.UnitCount)))
                throw ReconciliationRequired();
        }
    }

    private static bool Overlaps(int firstStart, int firstCount, int secondStart, int secondCount) =>
        firstStart < (long)secondStart + secondCount && secondStart < (long)firstStart + firstCount;

    private static ConflictException ReconciliationRequired() =>
        new("The original cash receipt has unresolved or inconsistent refund history.");
}
