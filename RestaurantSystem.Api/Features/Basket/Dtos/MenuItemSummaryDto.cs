namespace RestaurantSystem.Api.Features.Basket.Dtos;

public class MenuItemSummaryDto
{
    public string ProductName { get; set; } = null!;
    public string? VariationName { get; set; }
    public decimal? SpecialPrice { get; set; }
}
