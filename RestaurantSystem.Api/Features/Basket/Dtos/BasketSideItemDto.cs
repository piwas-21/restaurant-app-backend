namespace RestaurantSystem.Api.Features.Basket.Dtos;

public record BasketSideItemDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public decimal Price { get; set; }
    public string? ImageUrl { get; set; }
    public int Quantity { get; set; }
    public decimal SubTotal { get; set; }
}
