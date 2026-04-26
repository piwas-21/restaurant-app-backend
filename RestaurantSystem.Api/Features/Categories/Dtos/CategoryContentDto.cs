namespace RestaurantSystem.Api.Features.Categories.Dtos;

public record CategoryContentDto
{
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
}
