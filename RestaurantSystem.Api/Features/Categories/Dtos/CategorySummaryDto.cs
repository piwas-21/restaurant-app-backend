namespace RestaurantSystem.Api.Features.Categories.Dtos;

public record CategorySummaryDto
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public bool IsActive { get; init; }
    public int ProductCount { get; init; }
}
