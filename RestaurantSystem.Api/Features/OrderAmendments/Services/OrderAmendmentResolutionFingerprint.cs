using System.Text.Json.Serialization;
using RestaurantSystem.Api.Features.AccountPayments.Services;
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
            plan.Legs.OrderBy(value => value.Payment.Id).Select(ToLeg).ToArray(),
            plan.Loyalty is null ? null : OrderAmendmentJson.Hash(OrderAmendmentJson.Serialize(plan.Loyalty)));
        var cashRefunds = plan.Legs.Where(value => value.CashRefund is not null)
            .OrderBy(value => value.Payment.Id)
            .Select(value => new CashRefundFingerprint(value.Payment.Id, value.CashRefund!))
            .ToArray();
        return cashRefunds.Length == 0
            ? OrderAmendmentJson.Hash(OrderAmendmentJson.Serialize(snapshot))
            : OrderAmendmentJson.Hash(OrderAmendmentJson.Serialize(
                new CashBoundQuoteFingerprint(snapshot, cashRefunds)));
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
        IReadOnlyList<LegFingerprint> Legs, string? LoyaltyFingerprint);

    private sealed record LegFingerprint(Guid PaymentId, string PaymentMethod, string Custody,
        long AmountMinor, Guid? AttemptId, string? AccountId, bool? LiveMode,
        string? ChargeId, string? IntentId, IReadOnlyList<OrderAmendmentRefundScope> Scopes);

    private sealed record RequestFingerprint(Guid ActorId, Guid OrderId, Guid AmendmentId,
        Guid ClientOperationId, [property: JsonPropertyName("quoteHash")] string RequestQuoteHash, DateTime ExpiresAt,
        int ExpectedOrderVersion, long? ExpectedAccountRevision, string Currency,
        IReadOnlyList<ManualRefundSelectionRequest> ManualRefunds);

    private sealed record CashBoundQuoteFingerprint(
        QuoteFingerprint Quote, IReadOnlyList<CashRefundFingerprint> CashRefunds);

    private sealed record CashRefundFingerprint(Guid PaymentId, AccountCashRefundPlan Plan);
}
