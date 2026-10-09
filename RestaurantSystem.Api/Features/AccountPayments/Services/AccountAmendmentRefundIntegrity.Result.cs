using RestaurantSystem.Api.Features.OrderAmendments.Dtos;
using RestaurantSystem.Api.Features.OrderAmendments.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.AccountPayments.Services;

internal static partial class AccountAmendmentRefundIntegrity
{
    private static void ValidateResult(OrderAmendmentResolutionOperation operation,
        OrderAmendmentFinancialPreviewDto financial)
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
            || result.Loyalty != financial.Loyalty
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
}
