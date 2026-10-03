using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.Api.Features.AccountPayments.Dtos;

/// <summary>A payer capability reveals only its contribution, never the full visit or provider identifiers.</summary>
public sealed record AccountPaymentReceiptDto(
    Guid AttemptId,
    long AmountMinor,
    string Currency,
    AccountPaymentState State,
    long ReceivedMinor,
    long RefundedMinor,
    bool ReconciliationRequired,
    DateTime? CompletedAt);
