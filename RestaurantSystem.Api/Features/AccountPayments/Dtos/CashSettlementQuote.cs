using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.Api.Features.AccountPayments.Dtos;

/// <summary>Server-resolved tender terms frozen alongside an account payment quote.</summary>
public sealed record CashSettlementQuote(
    string PolicyVersion,
    string Currency,
    PaymentMethod PaymentMethod,
    long ExactAmountMinor,
    long AdjustmentMinor,
    long DueAmountMinor);
