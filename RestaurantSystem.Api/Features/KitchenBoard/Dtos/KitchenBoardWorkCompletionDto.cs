using System.Text.Json.Serialization;

namespace RestaurantSystem.Api.Features.KitchenBoard.Dtos;

public sealed record CompleteKitchenBoardWorkRequest(
    string Kind,
    [property: JsonRequired] int ExpectedOrderVersion,
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
