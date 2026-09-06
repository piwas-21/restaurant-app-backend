namespace RestaurantSystem.Api.Features.Products.Dtos;

public class ProductImageDto
{
    public Guid Id { get; set; }
    public string Url { get; set; } = null!;

    /// <summary>The card-sized WebP derivative, when one exists; guests render this on menu cards.</summary>
    public string? CardUrl { get; set; }
    public string? AltText { get; set; }
    public bool IsPrimary { get; set; }
    public int SortOrder { get; set; }

    // Foreign key
    public Guid ProductId { get; set; }
}
