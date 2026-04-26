namespace RestaurantSystem.Api.Features.Categories.Dtos;

public record CategoryDetailDto : CategoryDto
{
    public List<CategoryProductDto> FeaturedProducts { get; init; } = new();
    public Dictionary<string, CategoryContentDto> Content { get; init; } = new();
}
