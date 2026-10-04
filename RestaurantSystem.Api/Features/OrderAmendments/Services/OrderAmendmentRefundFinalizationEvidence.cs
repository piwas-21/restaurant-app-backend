using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.AccountPayments.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.OrderAmendments.Services;

internal static class OrderAmendmentRefundFinalizationEvidence
{
    internal static void ValidateSucceededEvidence(ApplicationDbContext context,
        OrderAmendmentResolutionOperation operation, IReadOnlyCollection<OrderAmendmentRefundLeg> legs)
    {
        foreach (var leg in legs)
        {
            var evidence = context.OrderAmendmentRefundEvidence.Local
                .Where(value => value.RefundLegId == leg.Id).ToArray();
            if (leg.Custody == OrderAmendmentRefundCustody.ManualTill)
            {
                ValidateTillEvidence(operation, leg, evidence);
                continue;
            }
            ValidateProviderEvidence(operation, leg, evidence);
        }
    }

    private static void ValidateTillEvidence(OrderAmendmentResolutionOperation operation,
        OrderAmendmentRefundLeg leg, OrderAmendmentRefundEvidence[] evidence)
    {
        var scopes = OrderAmendmentJson.Deserialize<List<OrderAmendmentRefundScope>>(leg.FrozenScopesJson);
        if ((leg.AccountPaymentAttemptId is null) != (scopes.Count == 0) || leg.Attempts.Count != 0
            || scopes.Sum(value => value.AmountMinor) != leg.AmountMinor
            || scopes.Select(value => (value.AllocationId, value.StartOrdinal, value.UnitCount))
                .Distinct().Count() != scopes.Count
            || !AccountAmendmentRefundIntegrity.HasNoProviderContext(leg)
            || !OrderAmendmentTillReferencePolicy.IsValid(leg.ManualTillReference) || evidence.Length != 1
            || evidence[0].Kind != OrderAmendmentRefundEvidenceKind.ManualTillConfirmation
            || evidence[0].State != OrderAmendmentRefundLegState.Succeeded
            || evidence[0].AmountMinor != leg.AmountMinor || evidence[0].Currency != leg.Currency
            || evidence[0].TillReference != leg.ManualTillReference
            || evidence[0].ObservedAt != leg.ResolvedAt
            || evidence[0].ActorUserId != operation.ActorUserId
            || evidence[0].ActorRole != operation.ActorRole)
            throw new ConflictException("The till refund confirmation requires reconciliation.");

        if (leg.CashRefundIntent is AccountCashRefundIntent intent)
        {
            var expected = AccountCashRefundPlan.FromIntent(intent).SettlementQuote();
            AccountCashRefundIntentValidator.RequireReturnEvidence(intent, leg, operation,
                expected, intent.ReturnEvidence);
        }
    }

    private static void ValidateProviderEvidence(OrderAmendmentResolutionOperation operation,
        OrderAmendmentRefundLeg leg, OrderAmendmentRefundEvidence[] evidence)
    {
        OrderAmendmentRefundProviderProof.RequireStoredAttemptHistory(
            leg, operation, evidence, requireSuccess: true);
    }
}
