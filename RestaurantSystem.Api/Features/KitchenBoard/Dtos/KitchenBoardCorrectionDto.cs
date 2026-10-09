namespace RestaurantSystem.Api.Features.KitchenBoard.Dtos;

public sealed record KitchenBoardCorrectionDto(
    Guid WorkItemId,
    Guid OrderId,
    string OrderNumber,
    string Status,
    int OrderVersion,
    Guid? TableId,
    string? TableLabel,
    int? TableNumber,
    Guid? ServiceSessionId,
    Guid? AmendmentId,
    long? AccountRevision,
    string? Target,
    string Summary,
    bool Withdrawn,
    bool IsCompleted,
    bool CanComplete,
    string? RouteStatus,
    DateTime CreatedAt,
    IReadOnlyList<KitchenBoardChangeDto> Changes);
