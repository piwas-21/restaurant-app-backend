using RestaurantSystem.Api.Features.Products.Dtos;

namespace RestaurantSystem.Api.Features.Categories.Dtos;

public record CategoryProductDto
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public decimal BasePrice { get; init; }
    public List<ProductImageDto> Images { get; init; } = new();
    public bool IsAvailable { get; init; }
    public bool IsPrimaryCategory { get; init; }
    public int PreparationTimeMinutes { get; init; }
    // `Variations` removed 2026-07-29 with GET /api/Categories/{id}/products (plan §9.16): that
    // handler was its only producer. The surviving producer, GetCategoryByIdQuery, neither includes
    // nor sets Variations, so the field could only ever serialize as [] — a permanently unfillable
    // field reads as "this product has no variations", which is a wrong answer rather than a missing
    // one. Re-add it with a producer, not before.
}
