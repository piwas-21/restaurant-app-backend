using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Api.Features.AccountPayments.Services;

namespace RestaurantSystem.Api.Features.OrderAmendments.Services;

internal sealed record OrderAmendmentResolutionPlan(
    string Currency,
    long CreditMinor,
    long RefundMinor,
    long UnpaidWaivedMinor,
    IReadOnlyList<OrderAmendmentRefundLegPlan> Legs);

internal sealed record OrderAmendmentRefundLegPlan(
    OrderPayment Payment,
    Guid? AccountPaymentAttemptId,
    OrderAmendmentRefundCustody Custody,
    long AmountMinor,
    IReadOnlyList<OrderAmendmentRefundScope> Scopes,
    string? ProviderAccountId,
    bool? ProviderLiveMode,
    string? ProviderChargeId,
    string? ProviderIntentId,
    AccountCashRefundPlan? CashRefund = null);

internal sealed record OrderAmendmentRefundScope(
    Guid AllocationId,
    Guid OrderId,
    Guid? OrderItemId,
    int StartOrdinal,
    int UnitCount,
    long MinorPerUnit,
    long AmountMinor);
