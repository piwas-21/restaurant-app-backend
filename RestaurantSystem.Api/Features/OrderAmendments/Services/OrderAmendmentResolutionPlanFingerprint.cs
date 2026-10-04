using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.OrderAmendments.Services;

internal static class OrderAmendmentResolutionPlanFingerprint
{
    internal static string Create(OrderAmendmentResolutionPlan plan) => Hash(new PlanSnapshot(
        plan.Currency, plan.CreditMinor, plan.RefundMinor, plan.UnpaidWaivedMinor,
        plan.Legs.OrderBy(value => value.Payment.Id).Select(value => new LegSnapshot(
            value.Payment.Id, value.AccountPaymentAttemptId, value.Custody.ToString(),
            value.AmountMinor, plan.Currency,
            value.Scopes.OrderBy(scope => scope.AllocationId).ThenBy(scope => scope.StartOrdinal)
                .Select(ScopeSnapshot.From).ToArray(), value.ProviderAccountId,
            value.ProviderLiveMode, value.ProviderChargeId, value.ProviderIntentId, null)).ToArray()));

    internal static string Create(OrderAmendmentResolutionOperation operation,
        IReadOnlyCollection<OrderAmendmentRefundLeg> legs) => Hash(new PlanSnapshot(
        operation.Currency, operation.CreditMinor, operation.RefundMinor, operation.UnpaidWaivedMinor,
        legs.OrderBy(value => value.SourcePaymentId).Select(value => new LegSnapshot(
            value.SourcePaymentId, value.AccountPaymentAttemptId, value.Custody.ToString(),
            value.AmountMinor, value.Currency,
            OrderAmendmentJson.Deserialize<List<OrderAmendmentRefundScope>>(value.FrozenScopesJson)
                .OrderBy(scope => scope.AllocationId).ThenBy(scope => scope.StartOrdinal)
                .Select(scope => new ScopeSnapshot(scope.AllocationId, scope.OrderId,
                    scope.OrderItemId, scope.StartOrdinal, scope.UnitCount,
                    scope.MinorPerUnit, scope.AmountMinor)).ToArray(), value.ProviderAccountId,
            value.ProviderLiveMode, value.ProviderChargeId, value.ProviderIntentId,
            null)).ToArray()));

    private static string Hash(PlanSnapshot value) => OrderAmendmentJson.Hash(OrderAmendmentJson.Serialize(value));

    private sealed record PlanSnapshot(
        string Currency, long CreditMinor, long RefundMinor, long UnpaidWaivedMinor,
        IReadOnlyList<LegSnapshot> Legs);

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
