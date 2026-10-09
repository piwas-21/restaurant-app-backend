namespace RestaurantSystem.Api.Features.KitchenBoard.Dtos;

public sealed record KitchenBoardPageDto<T>(
    IReadOnlyList<T> Items,
    int TotalCount,
    bool HasMore,
    IReadOnlyList<Guid> RemovedIds,
    string? NextCursor,
    long Watermark,
    string Mode);
