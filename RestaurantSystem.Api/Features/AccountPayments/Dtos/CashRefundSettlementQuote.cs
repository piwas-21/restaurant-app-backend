using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.Api.Features.AccountPayments.Dtos;

/// <summary>Frozen cash-drawer delta when exact account value is refunded.</summary>
public sealed record CashRefundSettlementQuote(
    string PolicyVersion,
    string Currency,
    PaymentMethod PaymentMethod,
    long ExactRefundAmountMinor,
    long RefundAdjustmentMinor,
    long CashRefundAmountMinor,
    long RetainedExactAmountMinor,
    long RetainedCashDueMinor);
