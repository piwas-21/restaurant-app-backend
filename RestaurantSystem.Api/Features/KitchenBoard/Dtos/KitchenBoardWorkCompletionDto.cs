namespace RestaurantSystem.Api.Features.KitchenBoard.Dtos;

public sealed record CompleteKitchenBoardWorkRequest(
    string Kind,
    int ExpectedOrderVersion,
    long? ExpectedAccountRevision);

public sealed record KitchenBoardWorkCompletionDto(
    Guid OrderId,
    Guid WorkItemId,
    string Kind,
    long? AccountRevision,
    int AcknowledgedOrderVersion,
    long Sequence,
    DateTime CompletedAt,
    bool IsCompleted);
