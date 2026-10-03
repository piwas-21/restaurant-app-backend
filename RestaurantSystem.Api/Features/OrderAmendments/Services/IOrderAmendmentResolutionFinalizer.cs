namespace RestaurantSystem.Api.Features.OrderAmendments.Services;

public interface IOrderAmendmentResolutionFinalizer
{
    Task TryFinalizeAsync(Guid operationId, Guid actorId, CancellationToken cancellationToken);
}
