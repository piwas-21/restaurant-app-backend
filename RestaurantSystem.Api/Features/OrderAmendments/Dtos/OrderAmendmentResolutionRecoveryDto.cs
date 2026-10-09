namespace RestaurantSystem.Api.Features.OrderAmendments.Dtos;

public sealed record OrderAmendmentResolutionRecoveryDto(
    OrderAmendmentResolutionStartRequest OriginalRequest,
    OrderAmendmentResolutionQuoteDto ReviewedQuote,
    OrderAmendmentResolutionResultDto Result);
