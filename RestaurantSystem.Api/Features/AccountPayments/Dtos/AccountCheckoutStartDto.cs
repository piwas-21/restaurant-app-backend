using System.Text.Json.Serialization;
using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.Api.Features.AccountPayments.Dtos;

public sealed record AccountCheckoutStartDto(
    Guid AttemptId, Guid OperationId, AccountPaymentState State, int Version,
    long AmountMinor, string Currency, DateTime ExpiresAt, string? CheckoutUrl,
    bool ReconciliationRequired, long ReceivedMinor, long RefundedMinor);

public sealed record StartAccountCheckoutRequest([property: JsonRequired] int ExpectedVersion);
