
namespace RestaurantSystem.Api.Features.Products.Dtos.Requests;

public record UploadProductImageDto
{
    public IFormFile Image { get; init; } = null!;
    public string? AltText { get; init; }
    public bool IsPrimary { get; init; }
    public int? SortOrder { get; init; }

}
