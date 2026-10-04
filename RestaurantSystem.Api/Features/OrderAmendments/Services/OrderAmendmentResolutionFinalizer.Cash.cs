using RestaurantSystem.Api.Features.AccountPayments.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.OrderAmendments.Services;

public sealed partial class OrderAmendmentResolutionFinalizer
{
    private static void ValidateCashRefundIntents(
        OrderAmendmentResolutionOperation operation, IReadOnlyCollection<OrderAmendmentRefundLeg> legs,
        IReadOnlyDictionary<Guid, AccountCashRefundHistory> historyByAttempt)
    {
        foreach (var leg in legs)
        {
            var intent = leg.CashRefundIntent;
            if (leg.AccountPaymentAttemptId is not Guid attemptId
                || !historyByAttempt.TryGetValue(attemptId, out var history))
            {
                if (intent is not null)
                    throw AccountCashRefundIntentValidator.ReconciliationRequired();
                continue;
            }

            if (intent?.CollectionReceipt is null)
                throw AccountCashRefundIntentValidator.ReconciliationRequired();
            var expected = AccountCashRefundIntentValidator.RequireMatchesHistory(intent, leg,
                operation, intent.CollectionReceipt, history);
            if (leg.State == OrderAmendmentRefundLegState.Succeeded)
                AccountCashRefundIntentValidator.RequireReturnEvidence(intent, leg, operation,
                    expected, intent.ReturnEvidence);
            else if (intent.ReturnEvidence is not null)
                throw AccountCashRefundIntentValidator.ReconciliationRequired();
        }
    }
}
