namespace RestaurantSystem.Api.Features.Categories.Dtos.Requests;

public record UpdateCategoryImageRequest
{
    public IFormFile Image { get; init; } = null!;
}
