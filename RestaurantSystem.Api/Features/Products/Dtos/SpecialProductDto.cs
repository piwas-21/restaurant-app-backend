namespace RestaurantSystem.Api.Features.Products.Dtos;

/// <summary>
/// DTO for special menu products
/// </summary>
public record SpecialProductDto
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public decimal BasePrice { get; init; }
    public string? ImageUrl { get; init; }
    public bool IsActive { get; init; }
    public bool IsAvailable { get; init; }
    public bool IsSpecial { get; init; }
    public bool IsFeaturedSpecial { get; init; }
    public DateTime? FeaturedDate { get; init; }
    public int DisplayOrder { get; init; }
}
