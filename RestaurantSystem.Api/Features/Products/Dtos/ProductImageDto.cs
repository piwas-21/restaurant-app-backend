namespace RestaurantSystem.Api.Features.Products.Dtos;

public class ProductImageDto
{
    public Guid Id { get; set; }
    public string Url { get; set; } = null!;
    public string? AltText { get; set; }
    public bool IsPrimary { get; set; } = false;
    public int SortOrder { get; set; }

    // Foreign key
    public Guid ProductId { get; set; }
}
