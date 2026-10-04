namespace RestaurantSystem.Api.Features.OrderAmendments.Dtos;

public sealed record OrderAmendmentEligibilityDto(
    Guid OrderId,
    int OrderVersion,
    long? AccountRevision,
    bool CanCreateAmendment,
    string? ReasonCode,
    string AmendmentMode);
