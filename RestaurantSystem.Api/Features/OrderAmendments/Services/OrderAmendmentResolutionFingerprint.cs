using RestaurantSystem.Api.Features.OrderAmendments.Dtos;

namespace RestaurantSystem.Api.Features.OrderAmendments.Services;

internal static class OrderAmendmentResolutionFingerprint
{
    internal static string QuoteHash(Guid actorId, Guid orderId, Guid amendmentId,
        OrderAmendmentResolutionQuoteRequest request, DateTime expiresAt,
        OrderAmendmentResolutionPlan plan)
    {
        var snapshot = new QuoteFingerprint(actorId, orderId, amendmentId, request.ClientOperationId,
            request.ExpectedOrderVersion, request.ExpectedAccountRevision, request.Currency,
            expiresAt, request.ManualRefunds.OrderBy(value => value.PaymentId).ToArray(),
            plan.Currency, plan.CreditMinor, plan.RefundMinor, plan.UnpaidWaivedMinor,
            plan.Legs.OrderBy(value => value.Payment.Id).Select(ToLeg).ToArray());
        return OrderAmendmentJson.Hash(OrderAmendmentJson.Serialize(snapshot));
    }

    internal static string RequestHash(Guid actorId, Guid orderId, Guid amendmentId,
        OrderAmendmentResolutionStartRequest request)
    {
        var snapshot = new RequestFingerprint(actorId, orderId, amendmentId,
            request.Quote.ClientOperationId, request.QuoteHash, request.ExpiresAt,
            request.Quote.ExpectedOrderVersion, request.Quote.ExpectedAccountRevision,
            request.Quote.Currency,
            request.Quote.ManualRefunds.OrderBy(value => value.PaymentId).ToArray());
        return OrderAmendmentJson.Hash(OrderAmendmentJson.Serialize(snapshot));
    }

    private static LegFingerprint ToLeg(OrderAmendmentRefundLegPlan leg) => new(
        leg.Payment.Id, leg.Payment.PaymentMethod.ToString(), leg.Custody.ToString(), leg.AmountMinor,
        leg.AccountPaymentAttemptId, leg.ProviderAccountId, leg.ProviderLiveMode,
        leg.ProviderChargeId, leg.ProviderIntentId,
        leg.Scopes.OrderBy(value => value.AllocationId).ThenBy(value => value.StartOrdinal).ToArray());

    private sealed record QuoteFingerprint(Guid ActorId, Guid OrderId, Guid AmendmentId,
        Guid ClientOperationId, int ExpectedOrderVersion, long? ExpectedAccountRevision,
        string Currency, DateTime ExpiresAt, IReadOnlyList<ManualRefundSelectionRequest> ManualRefunds,
        string PlanCurrency, long CreditMinor, long RefundMinor, long WaivedMinor,
        IReadOnlyList<LegFingerprint> Legs);

    private sealed record LegFingerprint(Guid PaymentId, string PaymentMethod, string Custody,
        long AmountMinor, Guid? AttemptId, string? AccountId, bool? LiveMode,
        string? ChargeId, string? IntentId, IReadOnlyList<OrderAmendmentRefundScope> Scopes);

    private sealed record RequestFingerprint(Guid ActorId, Guid OrderId, Guid AmendmentId,
        Guid ClientOperationId, string QuoteHash, DateTime ExpiresAt,
        int ExpectedOrderVersion, long? ExpectedAccountRevision, string Currency,
        IReadOnlyList<ManualRefundSelectionRequest> ManualRefunds);
}
