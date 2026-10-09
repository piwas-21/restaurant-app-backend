using RestaurantSystem.Api.Features.AccountPayments.Services;
using RestaurantSystem.Api.Features.OrderAmendments.Dtos;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.OrderAmendments.Services;

internal static class OrderAmendmentCashRefundMapper
{
    internal static CashRefundQuoteDto? Map(AccountCashRefundPlan? plan) => plan is null
        ? null
        : new CashRefundQuoteDto(plan.PolicyVersion, plan.OriginalExactAmountMinor,
            plan.OriginalDueAmountMinor, plan.PreviouslyRefundedExactMinor,
            plan.PreviouslyRefundedCashMinor, plan.ExactRefundAmountMinor,
            plan.RefundAdjustmentMinor, plan.CashRefundAmountMinor,
            plan.RetainedExactAmountMinor, plan.RetainedCashDueMinor);

    internal static CashRefundQuoteDto? Map(AccountCashRefundIntent? intent) => intent is null
        ? null
        : Map(AccountCashRefundPlan.FromIntent(intent));

    internal static CashReturnEvidenceDto? Map(AccountCashRefundEvidence? evidence) => evidence is null
        ? null
        : new CashReturnEvidenceDto(evidence.ExactRefundAmountMinor,
            evidence.RefundAdjustmentMinor, evidence.CashReturnedMinor, evidence.ObservedAt);
}
