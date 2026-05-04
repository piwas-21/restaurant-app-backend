namespace RestaurantSystem.Api.Features.Products.Dtos;

public record MenuSectionDto
{
    public Guid? Id { get; init; }
    public string Name { get; init; } = null!;
    public string? Description { get; init; }
    public int DisplayOrder { get; init; }

    public bool IsRequired { get; init; }
    public int MinSelection { get; init; }
    public int MaxSelection { get; init; }

    public List<MenuSectionItemDto> Items { get; init; } = new();
}
