namespace RestaurantSystem.Api.Features.Categories.Dtos;

// RestaurantSystem.Api/Features/Categories/Dtos/CategoryOrderDto.cs
public record CategoryOrderDto
{
    public Guid CategoryId { get; init; }
    public int DisplayOrder { get; init; }
}
