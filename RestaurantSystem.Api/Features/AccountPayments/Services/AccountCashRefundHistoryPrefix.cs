using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.OrderAmendments.Dtos;
using RestaurantSystem.Api.Features.OrderAmendments.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.AccountPayments.Services;

internal sealed record AccountCashRefundPendingTailScope(
    IReadOnlyList<AccountPaymentAllocationReversal> HistoryReversals,
    IReadOnlyDictionary<Guid, AccountPaymentAllocation> Allocations,
    IReadOnlySet<Guid> PriorOperationIds,
    IReadOnlySet<Guid> TargetSourceOrderIds);

internal sealed record AccountCashRefundPendingTailProof(
    AccountCashCollectionReceipt Receipt,
    Guid AttemptServiceSessionId,
    AccountCashRefundHistory History,
    IReadOnlyList<OrderAmendmentRefundEvidence> Evidence,
    IReadOnlyList<AccountPaymentAllocationReversal> TailReversals,
    AccountCashRefundPendingTailScope Scope);

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
        OrderAmendmentResolutionOperation operation, AccountCashRefundPendingTailProof proof)
    {
        var tailScope = proof.Scope;
        if (operation.Id != intent.OperationId || tailScope.PriorOperationIds.Contains(operation.Id)
            || operation.SourceOrderId == Guid.Empty || tailScope.TargetSourceOrderIds.Contains(operation.SourceOrderId)
            || operation.ServiceSessionId != proof.AttemptServiceSessionId
            || operation.State != OrderAmendmentResolutionOperationState.Processing
            || operation.ResolvedAt is not null || operation.FailureCode is not null
            || operation.ResultJson is not null
            || leg.State != OrderAmendmentRefundLegState.Pending || leg.ResolvedAt is not null
            || leg.FailureCode is not null || leg.ManualTillReference is not null
            || leg.Custody != OrderAmendmentRefundCustody.ManualTill
            || !AccountAmendmentRefundIntegrity.HasNoProviderContext(leg)
            || intent.ReturnEvidence is not null || proof.Evidence.Count != 0 || proof.TailReversals.Count != 0)
            throw ReconciliationRequired();

        AccountCashRefundIntentValidator.RequireMatchesHistory(intent, leg, operation, proof.Receipt, proof.History);
        var scopes = OrderAmendmentJson.Deserialize<List<OrderAmendmentRefundScope>>(leg.FrozenScopesJson);
        if (scopes.Count == 0 || scopes.Sum(value => value.AmountMinor) != leg.AmountMinor)
            throw ReconciliationRequired();

        for (var index = 0; index < scopes.Count; index++)
        {
            var scope = scopes[index];
            if (!tailScope.Allocations.TryGetValue(scope.AllocationId, out var allocation)
                || scope.OrderId != operation.SourceOrderId
                || allocation.AttemptId != proof.Receipt.AttemptId || allocation.OrderPaymentId != leg.SourcePaymentId
                || allocation.OrderId != scope.OrderId || allocation.OrderItemId != scope.OrderItemId
                || allocation.MinorPerUnit != scope.MinorPerUnit || scope.MinorPerUnit <= 0
                || scope.AmountMinor <= 0 || scope.UnitCount <= 0
                || scope.StartOrdinal < allocation.StartOrdinal
                || (long)scope.StartOrdinal + scope.UnitCount > (long)allocation.StartOrdinal + allocation.UnitCount
                || scope.AmountMinor != checked(scope.UnitCount * scope.MinorPerUnit))
                throw ReconciliationRequired();

            if (scopes.Take(index).Any(previous => previous.AllocationId == scope.AllocationId
                    && Overlaps(previous.StartOrdinal, previous.UnitCount, scope.StartOrdinal, scope.UnitCount))
                || tailScope.HistoryReversals.Any(value => value.AllocationId == scope.AllocationId
                    && Overlaps(value.StartOrdinal, value.UnitCount, scope.StartOrdinal, scope.UnitCount)))
                throw ReconciliationRequired();
        }
    }

    private static bool Overlaps(int firstStart, int firstCount, int secondStart, int secondCount) =>
        firstStart < (long)secondStart + secondCount && secondStart < (long)firstStart + firstCount;

    private static ConflictException ReconciliationRequired() =>
        new("The original cash receipt has unresolved or inconsistent refund history.");
}
