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
    IReadOnlyList<AccountPaymentAllocationDto> Allocations);
