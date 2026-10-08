namespace RestaurantSystem.Api.Features.KitchenBoard.Dtos;

public sealed record KitchenBoardItemDto(
    Guid ItemId,
    string ProductName,
    string? VariationName,
    int Quantity,
    string? Kind,
    string? SpecialInstructions,
    IReadOnlyList<KitchenBoardIngredientDto> Ingredients,
    IReadOnlyList<KitchenBoardItemDto> Children);

public sealed record KitchenBoardIngredientDto(
    Guid IngredientId,
    string IngredientName,
    int Quantity,
    bool IsRemoved,
    bool IsAddOn);
