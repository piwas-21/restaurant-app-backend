namespace RestaurantSystem.Api.Features.KitchenBoard.Dtos;

public sealed record KitchenBoardOrderDto(
    Guid OrderId,
    string OrderNumber,
    string Type,
    string Status,
    Guid? TableId,
    string? TableLabel,
    int? TableNumber,
    Guid? ServiceSessionId,
    DateTime CreatedAt,
    int Version,
    bool IsCompleted,
    DateTime? CompletedAt,
    bool CanComplete,
    IReadOnlyList<KitchenBoardRouteDto> RequiredKitchenRoutes,
    IReadOnlyList<KitchenBoardItemDto> Items);

public sealed record KitchenBoardRouteDto(string Target, string Status);
