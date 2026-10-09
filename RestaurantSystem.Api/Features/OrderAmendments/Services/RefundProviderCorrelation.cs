namespace RestaurantSystem.Api.Features.OrderAmendments.Services;

internal sealed record RefundProviderCorrelation(
    IReadOnlyDictionary<Guid, Guid> OperationByLeg,
    IReadOnlyDictionary<Guid, Guid> LegByAttempt,
    Guid? AdoptOperationId = null,
    Guid? AdoptLegId = null,
    Guid? AdoptAttemptId = null);
