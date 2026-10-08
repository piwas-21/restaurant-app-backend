namespace RestaurantSystem.Api.Features.KitchenBoard.Dtos;

public sealed record KitchenBoardChangeDto(
    string Kind,
    Guid? ReplacementDispatchedOrderId,
    string? ReplacementDispatchedOrderNumber,
    KitchenBoardItemDto? Previous,
    KitchenBoardItemDto? Current);
