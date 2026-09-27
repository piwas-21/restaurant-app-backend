namespace RestaurantSystem.Api.Features.Products.Dtos;

public sealed record ProductQuoteDto
{
    public Guid ProductId { get; init; }
    public int Quantity { get; init; }
    /// <summary>Effective per-item price including selected modifications and customizations.</summary>
    public decimal UnitPrice { get; init; }
    public decimal TotalPrice { get; init; }
}
