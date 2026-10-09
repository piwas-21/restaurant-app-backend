using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.Api.Features.AccountPayments.Dtos;

public sealed record AccountPaymentOperationDto(
    Guid ServiceSessionId,
    Guid OperationId,
    AccountPaymentState State,
    int Version,
    long ExpectedAccountRevision,
    AccountPaymentMode Mode,
    PaymentMethod PaymentMethod,
    long AmountMinor,
    string Currency,
    DateTime QuoteExpiresAt,
    DateTime? ReservedAt,
    DateTime? ReservationExpiresAt,
    Guid? EqualSharePlanId,
    int? EqualShareOrdinal,
    IReadOnlyList<AccountPaymentAllocationDto> Allocations)
{
    public CashSettlementQuote? CashSettlement { get; init; }
    public CashCollectionReceiptDto? CashReceipt { get; init; }
    public long TipMinor { get; init; }
    public Guid? CustomSharePlanId { get; init; }
    public int? CustomShareOrdinal { get; init; }
}
