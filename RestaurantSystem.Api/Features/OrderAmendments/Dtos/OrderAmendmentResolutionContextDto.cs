namespace RestaurantSystem.Api.Features.OrderAmendments.Dtos;

public sealed record OrderAmendmentResolutionContextDto(
    Guid OrderId,
    Guid AmendmentId,
    int ExpectedOrderVersion,
    long? ExpectedAccountRevision,
    string Currency,
    long CreditMinor,
    IReadOnlyList<ManualRefundCandidateDto> ManualRefundCandidates);

public sealed record ManualRefundCandidateDto(Guid PaymentId, string PaymentMethod, long AvailableMinor);
