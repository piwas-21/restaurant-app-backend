using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.OrderAmendments.Dtos;
using RestaurantSystem.Api.Features.OrderAmendments.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.AccountPayments.Services;

internal sealed record AccountAmendmentRefundSnapshot(
    IReadOnlyList<AccountPaymentAllocationReversal> Reversals,
    IReadOnlyDictionary<Guid, long> AuthorizedRefundMinorByPayment,
    IReadOnlyDictionary<Guid, AccountCashRefundHistory> CashRefundHistoryByAttempt);

/// <summary>Checks that every table-account refund is backed by one resolved amendment operation.</summary>
internal static class AccountAmendmentRefundIntegrity
{
    internal static async Task<AccountAmendmentRefundSnapshot> ReadAsync(
        ApplicationDbContext context,
        IReadOnlyList<Order> orders,
        IReadOnlyList<OrderAmendment> amendments,
        IReadOnlyList<AccountPaymentAttempt> attempts,
        AccountMoney money,
        CancellationToken cancellationToken,
        Guid? excludedOperationId = null)
    {
        var allocationIds = attempts.SelectMany(value => value.Allocations)
            .Select(value => value.Id).ToArray();
        var reversalRows = allocationIds.Length == 0 ? []
            : await context.AccountPaymentAllocationReversals.AsNoTracking()
                .Where(value => allocationIds.Contains(value.AllocationId)).ToListAsync(cancellationToken);
        var reversals = reversalRows.ToArray();
        var orderIds = orders.Select(value => value.Id).ToArray();
        var operations = new List<OrderAmendmentResolutionOperation>();
        if (orderIds.Length > 0)
        {
            var operationQuery = context.OrderAmendmentResolutionOperations.AsNoTracking()
                .Where(value => orderIds.Contains(value.SourceOrderId));
            if (excludedOperationId is Guid excludeId)
                operationQuery = operationQuery.Where(value => value.Id != excludeId);
            operations = await operationQuery.Include(value => value.Legs).ThenInclude(value => value.Attempts)
                .ToListAsync(cancellationToken);
        }
        var amendmentById = amendments.ToDictionary(value => value.Id);
        if (operations.Any(value => !amendmentById.ContainsKey(value.AmendmentId)))
            throw NeedsReconciliation();

        var legIds = operations.SelectMany(value => value.Legs).Select(value => value.Id).ToArray();
        if (reversals.Any(value => !legIds.Contains(value.RefundLegId)))
            throw NeedsReconciliation();
        var evidenceRows = legIds.Length == 0 ? []
            : await context.OrderAmendmentRefundEvidence.AsNoTracking()
                .Where(value => legIds.Contains(value.RefundLegId)).ToListAsync(cancellationToken);
        var evidence = evidenceRows.ToArray();
        ValidateResolvedAmendments(amendments, operations, evidence, reversals, attempts, money);
        var authorized = BuildAuthorizedRefunds(operations, orders, money);
        ValidateRefundedCapturedTenders(orders, operations, attempts, reversals, money);
        var cashHistory = await AccountCashRefundHistoryReader.ReadAsync(context,
            attempts.Select(value => value.Id).ToArray(), cancellationToken, excludedOperationId);
        return new AccountAmendmentRefundSnapshot(reversals, authorized, cashHistory);
    }

    private static void ValidateResolvedAmendments(
        IReadOnlyList<OrderAmendment> amendments,
        IReadOnlyList<OrderAmendmentResolutionOperation> operations,
        IReadOnlyList<OrderAmendmentRefundEvidence> evidence,
        AccountPaymentAllocationReversal[] reversals,
        IReadOnlyList<AccountPaymentAttempt> attempts,
        AccountMoney money)
    {
        var operationByAmendment = operations.ToDictionary(value => value.AmendmentId);
        foreach (var amendment in amendments)
        {
            var financial = OrderAmendmentFinancialGuard.ReadValidSnapshot(amendment.FinancialResolutionJson);
            if (financial.CreditState == OrderAmendmentCreditState.Resolved
                && !operationByAmendment.ContainsKey(amendment.Id))
                throw NeedsReconciliation();
            if (operationByAmendment.TryGetValue(amendment.Id, out var operation))
                ValidateOperation(operation, amendment, financial, evidence, reversals, attempts, money);
        }
    }

    private static void ValidateOperation(
        OrderAmendmentResolutionOperation operation,
        OrderAmendment amendment,
        OrderAmendmentFinancialPreviewDto financial,
        IReadOnlyList<OrderAmendmentRefundEvidence> evidence,
        IReadOnlyList<AccountPaymentAllocationReversal> reversals,
        IReadOnlyList<AccountPaymentAttempt> attempts,
        AccountMoney money)
    {
        if (operation.State != OrderAmendmentResolutionOperationState.Resolved
            || operation.ResolvedAt is null || operation.ActorUserId == Guid.Empty
            || operation.ActorRole != UserRole.Admin.ToString()
            || operation.ClientOperationId == Guid.Empty || operation.RequestHash.Length != 64
            || operation.Currency != money.Currency || operation.ServiceSessionId != amendment.ServiceSessionId
            || operation.SourceOrderId != amendment.SourceOrderId || amendment.State != OrderAmendmentState.Committed
            || financial.ResolutionStatus != OrderAmendmentFinancialResolutionStatus.Resolved
            || financial.CreditState != OrderAmendmentCreditState.Resolved
            || financial.RefundState != OrderAmendmentRefundState.Resolved
            || financial.LoyaltyState != OrderAmendmentLoyaltyState.None
            || financial.PotentialCreditMinor != operation.CreditMinor
            || operation.CreditMinor <= 0 || operation.RefundMinor < 0
            || operation.RefundMinor > operation.CreditMinor
            || operation.UnpaidWaivedMinor != operation.CreditMinor - operation.RefundMinor)
            throw NeedsReconciliation();

        var legs = operation.Legs.ToArray();
        if (legs.Sum(value => value.AmountMinor) != operation.RefundMinor
            || legs.Any(value => value.State != OrderAmendmentRefundLegState.Succeeded
                || value.ResolvedAt is null || value.Currency != money.Currency || value.AmountMinor <= 0))
            throw NeedsReconciliation();
        ValidateResult(operation);
        var allocationById = attempts.SelectMany(value => value.Allocations).ToDictionary(value => value.Id);
        foreach (var leg in legs)
            ValidateLeg(operation, leg, evidence.Where(value => value.RefundLegId == leg.Id).ToArray(),
                reversals.Where(value => value.RefundLegId == leg.Id).ToArray(), allocationById, money);
    }

    private static void ValidateResult(OrderAmendmentResolutionOperation operation)
    {
        if (string.IsNullOrWhiteSpace(operation.ResultJson))
            throw NeedsReconciliation();
        var result = OrderAmendmentJson.Deserialize<OrderAmendmentResolutionResultDto>(operation.ResultJson);
        if (result.RefundLegs is null)
            throw NeedsReconciliation();
        var resultLegs = result.RefundLegs.OrderBy(value => value.PaymentId).ToArray();
        var operationLegs = operation.Legs.OrderBy(value => value.SourcePaymentId).ToArray();
        if (result.OperationId != operation.Id || result.ClientOperationId != operation.ClientOperationId
            || result.AmendmentId != operation.AmendmentId || result.SourceOrderId != operation.SourceOrderId
            || result.State != OrderAmendmentResolutionOperationState.Resolved.ToString()
            || result.Currency != operation.Currency || result.CreditMinor != operation.CreditMinor
            || result.RefundMinor != operation.RefundMinor || result.UnpaidWaivedMinor != operation.UnpaidWaivedMinor
            || !PostgresTimestampPrecision.MatchesColumn(result.ResolvedAt, operation.ResolvedAt)
            || resultLegs.Length != operationLegs.Length
            || resultLegs.Where((value, index) =>
                value.PaymentId != operationLegs[index].SourcePaymentId
                || value.Custody != operationLegs[index].Custody.ToString()
                || value.State != OrderAmendmentRefundLegState.Succeeded.ToString()
                || value.AmountMinor != operationLegs[index].AmountMinor
                || !PostgresTimestampPrecision.MatchesColumn(
                    value.ResolvedAt, operationLegs[index].ResolvedAt)).Any())
            throw NeedsReconciliation();
    }

    private static void ValidateLeg(
        OrderAmendmentResolutionOperation operation, OrderAmendmentRefundLeg leg,
        OrderAmendmentRefundEvidence[] evidence,
        AccountPaymentAllocationReversal[] reversals,
        Dictionary<Guid, AccountPaymentAllocation> allocationById,
        AccountMoney money)
    {
        var scopes = OrderAmendmentJson.Deserialize<List<OrderAmendmentRefundScope>>(leg.FrozenScopesJson);
        if (!HasValidRefundScopeShape(leg, scopes))
            throw NeedsReconciliation();
        if (reversals.Length != scopes.Count)
            throw NeedsReconciliation();
        ValidateScopeReversals(operation, leg, scopes, reversals, allocationById, money);

        if (leg.Custody == OrderAmendmentRefundCustody.ManualTill)
        {
            var confirmation = evidence.SingleOrDefault(value =>
                value.Kind == OrderAmendmentRefundEvidenceKind.ManualTillConfirmation);
            if (leg.Attempts.Count != 0 || (leg.AccountPaymentAttemptId is null) != (scopes.Count == 0)
                || string.IsNullOrWhiteSpace(leg.ManualTillReference) || confirmation is null
            || evidence.Length != 1 || confirmation.State != OrderAmendmentRefundLegState.Succeeded
                || confirmation.AmountMinor != leg.AmountMinor || confirmation.Currency != money.Currency
                || confirmation.TillReference != leg.ManualTillReference
                || confirmation.ActorUserId != operation.ActorUserId
                || confirmation.ActorRole != operation.ActorRole || leg.ProviderChargeId is not null)
                throw NeedsReconciliation();
            return;
        }

        if (leg.Custody != OrderAmendmentRefundCustody.StripeDirect || scopes.Count == 0
            || leg.AccountPaymentAttemptId is null
            || !string.Equals(leg.Currency, money.Currency, StringComparison.OrdinalIgnoreCase))
            throw NeedsReconciliation();
        OrderAmendmentRefundProviderProof.RequireStoredAttemptHistory(
            leg, operation, evidence, requireSuccess: true);
    }

    private static void ValidateScopeReversals(
        OrderAmendmentResolutionOperation operation, OrderAmendmentRefundLeg leg,
        IReadOnlyList<OrderAmendmentRefundScope> scopes, AccountPaymentAllocationReversal[] reversals,
        Dictionary<Guid, AccountPaymentAllocation> allocationById, AccountMoney money)
    {
        foreach (var scope in scopes)
        {
            if (!allocationById.TryGetValue(scope.AllocationId, out var allocation)
                || allocation.AttemptId != leg.AccountPaymentAttemptId
                || allocation.OrderPaymentId != leg.SourcePaymentId
                || scope.OrderId != allocation.OrderId || scope.OrderItemId != allocation.OrderItemId
                || scope.MinorPerUnit != allocation.MinorPerUnit || scope.UnitCount <= 0
                || scope.StartOrdinal < allocation.StartOrdinal
                || (long)scope.StartOrdinal + scope.UnitCount > (long)allocation.StartOrdinal + allocation.UnitCount
                || scope.AmountMinor != checked(scope.MinorPerUnit * scope.UnitCount))
                throw NeedsReconciliation();
            var reversal = reversals.SingleOrDefault(value => value.AllocationId == scope.AllocationId
                && value.StartOrdinal == scope.StartOrdinal && value.UnitCount == scope.UnitCount);
            if (reversal is null || reversal.RefundLegId != leg.Id || reversal.OrderId != scope.OrderId
                || reversal.OrderItemId != scope.OrderItemId || reversal.MinorPerUnit != scope.MinorPerUnit
                || reversal.AmountMinor != scope.AmountMinor || reversal.Currency != money.Currency
                || reversal.ActorUserId != operation.ActorUserId || reversal.ActorRole != operation.ActorRole)
                throw NeedsReconciliation();
        }
    }

    private static Dictionary<Guid, long> BuildAuthorizedRefunds(
        IReadOnlyList<OrderAmendmentResolutionOperation> operations,
        IReadOnlyList<Order> orders, AccountMoney money)
    {
        var paymentById = orders.SelectMany(value => value.Payments).ToDictionary(value => value.Id);
        var amounts = new Dictionary<Guid, long>();
        foreach (var operation in operations)
        {
            foreach (var leg in operation.Legs)
            {
                if (!paymentById.TryGetValue(leg.SourcePaymentId, out var payment)
                    || payment.OrderId != operation.SourceOrderId || !payment.Status.IsCaptured())
                    throw NeedsReconciliation();
                amounts[leg.SourcePaymentId] = checked(
                    amounts.GetValueOrDefault(leg.SourcePaymentId) + leg.AmountMinor);
                if (amounts[leg.SourcePaymentId] > money.ToMinor(payment.Amount)
                    || money.ToMinor(payment.RefundedAmount ?? 0m) < amounts[leg.SourcePaymentId])
                    throw NeedsReconciliation();
            }
        }
        return amounts;
    }

    private static void ValidateRefundedCapturedTenders(
        IReadOnlyList<Order> orders, IReadOnlyList<OrderAmendmentResolutionOperation> operations,
        IReadOnlyList<AccountPaymentAttempt> attempts,
        IReadOnlyList<AccountPaymentAllocationReversal> reversals, AccountMoney money)
    {
        var allPayments = orders.SelectMany(value => value.Payments).ToDictionary(value => value.Id);
        var authorizedRefunds = operations.SelectMany(operation => operation.Legs
                .Select(leg => leg))
            .GroupBy(value => value.SourcePaymentId)
            .ToDictionary(group => group.Key, group => group.Sum(value => value.AmountMinor));
        var allocatedRefunds = operations.SelectMany(operation => operation.Legs
                .Where(leg => leg.AccountPaymentAttemptId.HasValue))
            .GroupBy(value => value.SourcePaymentId)
            .ToDictionary(group => group.Key, group => group.Sum(value => value.AmountMinor));
        foreach (var payment in allPayments.Values)
        {
            var actual = money.ToMinor(payment.RefundedAmount
                ?? (payment.IsRefunded || payment.Status == PaymentStatus.Refunded ? payment.Amount : 0m));
            var authorized = authorizedRefunds.GetValueOrDefault(payment.Id);
            if (actual != authorized)
                throw NeedsReconciliation();
        }

        var allocationPayments = attempts.Where(value => value.State == AccountPaymentState.Captured)
            .SelectMany(value => value.Allocations)
            .Where(value => value.OrderPaymentId.HasValue)
            .GroupBy(value => value.OrderPaymentId!.Value);
        var allocationById = attempts.SelectMany(attempt => attempt.Allocations).ToDictionary(value => value.Id);
        var reversedByPayment = reversals.GroupBy(value => allocationById.TryGetValue(value.AllocationId,
                out var allocation) && allocation.OrderPaymentId.HasValue
                    ? allocation.OrderPaymentId.Value : throw NeedsReconciliation())
            .ToDictionary(group => group.Key, group => group.Sum(value => value.AmountMinor));
        foreach (var group in allocationPayments)
        {
            if (!allPayments.TryGetValue(group.Key, out var payment)
                || payment.OrderId != group.First().OrderId
                || reversedByPayment.GetValueOrDefault(group.Key)
                    != allocatedRefunds.GetValueOrDefault(group.Key))
                throw NeedsReconciliation();
        }
        if (reversedByPayment.Any(value => value.Value != allocatedRefunds.GetValueOrDefault(value.Key)))
            throw NeedsReconciliation();
    }

    internal static bool HasValidRefundScopeShape(
        OrderAmendmentRefundLeg leg, IReadOnlyList<OrderAmendmentRefundScope> scopes)
    {
        if (leg.Custody == OrderAmendmentRefundCustody.ManualTill && !HasNoProviderContext(leg))
            return false;
        if (leg.Custody == OrderAmendmentRefundCustody.ManualTill
            && leg.AccountPaymentAttemptId is null)
            return scopes.Count == 0 && leg.Attempts.Count == 0;

        return leg.AccountPaymentAttemptId.HasValue && scopes.Count > 0
            && scopes.Sum(value => value.AmountMinor) == leg.AmountMinor
            && scopes.Select(value => (value.AllocationId, value.StartOrdinal, value.UnitCount))
                .Distinct().Count() == scopes.Count;
    }

    internal static bool HasNoProviderContext(OrderAmendmentRefundLeg leg) =>
        leg.ProviderAccountId is null && leg.ProviderLiveMode is null
        && leg.ProviderChargeId is null && leg.ProviderIntentId is null;

    private static ConflictException NeedsReconciliation() =>
        new("The account contains an amendment refund or captured allocation that requires reconciliation.");
}
