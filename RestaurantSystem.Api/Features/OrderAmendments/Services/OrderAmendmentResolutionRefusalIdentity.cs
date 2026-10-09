namespace RestaurantSystem.Api.Features.OrderAmendments.Services;

internal sealed record OrderAmendmentResolutionRefusalIdentity(
    Guid OrderId, Guid AmendmentId, string RequestHash, Guid ActorId);
