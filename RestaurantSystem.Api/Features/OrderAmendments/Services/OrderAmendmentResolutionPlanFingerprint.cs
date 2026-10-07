using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Api.Features.AccountPayments.Services;
using RestaurantSystem.Api.Common.Exceptions;
using System.Text.Json;

namespace RestaurantSystem.Api.Features.OrderAmendments.Services;

internal static class OrderAmendmentResolutionPlanFingerprint
{
    internal static string Create(OrderAmendmentResolutionPlan plan)
    {
        var snapshot = new PlanSnapshot(plan.Currency, plan.CreditMinor, plan.RefundMinor,
            plan.UnpaidWaivedMinor, plan.Legs.OrderBy(value => value.Payment.Id).Select(value => new LegSnapshot(
            value.Payment.Id, value.AccountPaymentAttemptId, value.Custody.ToString(),
            value.AmountMinor, plan.Currency,
            value.Scopes.OrderBy(scope => scope.AllocationId).ThenBy(scope => scope.StartOrdinal)
                .Select(ScopeSnapshot.From).ToArray(), value.ProviderAccountId,
            value.ProviderLiveMode, value.ProviderChargeId, value.ProviderIntentId, null)).ToArray(),
            LoyaltyFingerprint(plan.Loyalty));
        var cashRefunds = plan.Legs.Where(value => value.CashRefund is not null)
            .OrderBy(value => value.Payment.Id)
            .Select(value => new CashRefundFingerprint(value.Payment.Id, value.CashRefund!)).ToArray();
        return cashRefunds.Length == 0
            ? Hash(snapshot)
            : Hash(new CashBoundPlanFingerprint(snapshot, cashRefunds));
    }

    internal static string Create(OrderAmendmentResolutionOperation operation,
        IReadOnlyCollection<OrderAmendmentRefundLeg> legs)
    {
        OrderAmendmentResolutionSnapshot persisted;
        try { persisted = OrderAmendmentJson.Deserialize<OrderAmendmentResolutionSnapshot>(operation.SnapshotJson); }
        catch (JsonException exception)
        { throw new ConflictException("The frozen loyalty resolution evidence is unavailable.", exception); }
        var snapshot = new PlanSnapshot(operation.Currency, operation.CreditMinor,
            operation.RefundMinor, operation.UnpaidWaivedMinor,
            legs.OrderBy(value => value.SourcePaymentId).Select(value => new LegSnapshot(
            value.SourcePaymentId, value.AccountPaymentAttemptId, value.Custody.ToString(),
            value.AmountMinor, value.Currency,
            OrderAmendmentJson.Deserialize<List<OrderAmendmentRefundScope>>(value.FrozenScopesJson)
                .OrderBy(scope => scope.AllocationId).ThenBy(scope => scope.StartOrdinal)
                .Select(scope => new ScopeSnapshot(scope.AllocationId, scope.OrderId,
                    scope.OrderItemId, scope.StartOrdinal, scope.UnitCount,
                    scope.MinorPerUnit, scope.AmountMinor)).ToArray(), value.ProviderAccountId,
            value.ProviderLiveMode, value.ProviderChargeId, value.ProviderIntentId,
            null)).ToArray(), LoyaltyFingerprint(persisted.LoyaltyPlan));
        var cashRefunds = legs.Where(value => value.CashRefundIntent is not null)
            .OrderBy(value => value.SourcePaymentId)
            .Select(value => new CashRefundFingerprint(value.SourcePaymentId,
                AccountCashRefundPlan.FromIntent(value.CashRefundIntent!))).ToArray();
        if (persisted.LoyaltyPlanVersion is null)
        {
            var legacy = new LegacyPlanSnapshot(operation.Currency, operation.CreditMinor,
                operation.RefundMinor, operation.UnpaidWaivedMinor, snapshot.Legs);
            return cashRefunds.Length == 0
                ? Hash(legacy)
                : Hash(new LegacyCashBoundPlanFingerprint(legacy, cashRefunds));
        }
        return cashRefunds.Length == 0
            ? Hash(snapshot)
            : Hash(new CashBoundPlanFingerprint(snapshot, cashRefunds));
    }

    private static string Hash<T>(T value) => OrderAmendmentJson.Hash(OrderAmendmentJson.Serialize(value));

    private static string? LoyaltyFingerprint(OrderAmendmentLoyaltyPlan? value) => value is null
        || !value.SnapshotId.HasValue
        ? null : OrderAmendmentJson.Hash(OrderAmendmentJson.Serialize(value));

    private sealed record PlanSnapshot(
        string Currency, long CreditMinor, long RefundMinor, long UnpaidWaivedMinor,
        IReadOnlyList<LegSnapshot> Legs, string? LoyaltyFingerprint);

    private sealed record CashBoundPlanFingerprint(
        PlanSnapshot Plan, IReadOnlyList<CashRefundFingerprint> CashRefunds);

    private sealed record LegacyPlanSnapshot(
        string Currency, long CreditMinor, long RefundMinor, long UnpaidWaivedMinor,
        IReadOnlyList<LegSnapshot> Legs);

    private sealed record LegacyCashBoundPlanFingerprint(
        LegacyPlanSnapshot Plan, IReadOnlyList<CashRefundFingerprint> CashRefunds);

    private sealed record CashRefundFingerprint(Guid PaymentId, AccountCashRefundPlan Plan);

    private sealed record LegSnapshot(
        Guid PaymentId, Guid? AttemptId, string Custody, long AmountMinor, string Currency,
        IReadOnlyList<ScopeSnapshot> Scopes, string? ProviderAccountId, bool? ProviderLiveMode,
        string? ProviderChargeId, string? ProviderIntentId, string? TillReference);

    private sealed record ScopeSnapshot(
        Guid AllocationId, Guid OrderId, Guid? OrderItemId, int StartOrdinal,
        int UnitCount, long MinorPerUnit, long AmountMinor)
    {
        internal static ScopeSnapshot From(OrderAmendmentRefundScope value) => new(
            value.AllocationId, value.OrderId, value.OrderItemId, value.StartOrdinal,
            value.UnitCount, value.MinorPerUnit, value.AmountMinor);
    }
}
