using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.AccountPayments.Dtos;
using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.Api.Features.AccountPayments.Services;

/// <summary>Projects refunds against one original, frozen physical cash contribution.</summary>
internal static class AccountCashRefundPolicy
{
    internal static CashRefundSettlementQuote Project(
        CashSettlementQuote original, long previouslyRefundedExactMinor,
        long previouslyRefundedCashMinor, long refundExactMinor)
    {
        if (original is null || original.PaymentMethod != PaymentMethod.Cash
            || previouslyRefundedExactMinor < 0 || previouslyRefundedCashMinor < 0 || refundExactMinor <= 0
            || previouslyRefundedExactMinor > original.ExactAmountMinor)
            throw new BadRequestException("The cash refund amounts are outside the original contribution.");

        var retainedExactBefore = original.ExactAmountMinor - previouslyRefundedExactMinor;
        var retainedCashBefore = AccountCashSettlementPolicy.RetainedDue(original, retainedExactBefore);
        if (original.DueAmountMinor - previouslyRefundedCashMinor != retainedCashBefore)
            throw new ConflictException("Prior cash refunds do not reconcile with the original receipt.");

        if (refundExactMinor > original.ExactAmountMinor - previouslyRefundedExactMinor)
            throw new BadRequestException("The cash refund exceeds the original exact contribution.");
        var totalRefundedExact = previouslyRefundedExactMinor + refundExactMinor;

        var retainedExact = original.ExactAmountMinor - totalRefundedExact;
        var retainedCashDue = AccountCashSettlementPolicy.RetainedDue(original, retainedExact);
        var cashRefund = retainedCashBefore - retainedCashDue;
        if (cashRefund < 0 || cashRefund > original.DueAmountMinor - previouslyRefundedCashMinor)
            throw new ConflictException("The cash refund does not reconcile with the original receipt.");

        return new CashRefundSettlementQuote(
            original.PolicyVersion,
            original.Currency,
            original.PaymentMethod,
            refundExactMinor,
            cashRefund - refundExactMinor,
            cashRefund,
            retainedExact,
            retainedCashDue);
    }
}
