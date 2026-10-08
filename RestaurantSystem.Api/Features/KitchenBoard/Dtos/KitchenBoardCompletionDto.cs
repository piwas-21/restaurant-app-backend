namespace RestaurantSystem.Api.Features.KitchenBoard.Dtos;

public sealed record KitchenBoardCompletionDto(
    Guid OrderId,
    Guid WorkItemId,
    string Kind,
    long? AccountRevision,
    int AcknowledgedOrderVersion,
    long Sequence,
    DateTime CompletedAt);
