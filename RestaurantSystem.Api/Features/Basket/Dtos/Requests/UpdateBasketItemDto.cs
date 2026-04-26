namespace RestaurantSystem.Api.Features.Basket.Dtos.Requests;

public record UpdateBasketItemDto
{
    public int Quantity { get; init; }
    public string? SpecialInstructions { get; init; }
}
