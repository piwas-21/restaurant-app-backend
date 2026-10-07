using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.AccountPayments.Services;
using RestaurantSystem.Api.Features.OrderAmendments.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.OrderAmendments.Services;

public sealed partial class OrderAmendmentResolutionFinalizer
{
    private static void ApplyFinancialResolution(OrderAmendment amendment,
        OrderAmendmentResolutionOperation operation, AccountMoney money,
        OrderAmendmentLoyaltyResultDto? loyalty)
    {
        var preview = OrderAmendmentJson.Deserialize<OrderAmendmentFinancialPreviewDto>(
            amendment.FinancialResolutionJson);
        if (preview.Currency != operation.Currency
            || preview.PotentialCreditMinor != operation.CreditMinor
            || preview.PotentialCreditMinor <= 0
            || operation.RefundMinor < 0 || operation.RefundMinor > operation.CreditMinor
            || operation.UnpaidWaivedMinor != operation.CreditMinor - operation.RefundMinor
            || money.Currency != operation.Currency
            || money.ToMinor(money.ToMajor(operation.CreditMinor)) != operation.CreditMinor)
            throw new ConflictException("The frozen financial resolution does not match its refund operation.");
        amendment.FinancialResolutionJson = OrderAmendmentJson.Serialize(preview with
        {
            ResolutionStatus = OrderAmendmentFinancialResolutionStatus.Resolved,
            CreditState = OrderAmendmentCreditState.Resolved,
            LoyaltyState = loyalty?.State == OrderAmendmentLoyaltyOperationStatus.Resolved
                ? OrderAmendmentLoyaltyState.Resolved : OrderAmendmentLoyaltyState.None,
            RefundState = OrderAmendmentRefundState.Resolved,
            Loyalty = loyalty
        });
    }
}
