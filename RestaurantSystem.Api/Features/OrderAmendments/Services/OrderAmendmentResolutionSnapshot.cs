using RestaurantSystem.Api.Features.OrderAmendments.Dtos;

namespace RestaurantSystem.Api.Features.OrderAmendments.Services;

internal sealed record OrderAmendmentResolutionSnapshot(
    string QuoteHash,
    DateTime ExpiresAt,
    string Currency,
    long CreditMinor,
    long RefundMinor,
    long UnpaidWaivedMinor,
    string RequestHash,
    string SourceFinancialFingerprint,
    string PlanFingerprint,
    OrderAmendmentResolutionStartRequest? OriginalRequest = null,
    OrderAmendmentResolutionQuoteDto? ReviewedQuote = null);
