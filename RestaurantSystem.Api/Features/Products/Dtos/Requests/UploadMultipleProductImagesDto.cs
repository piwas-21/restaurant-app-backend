
namespace RestaurantSystem.Api.Features.Products.Dtos.Requests;

public record UploadMultipleProductImagesDto
{
    public List<IFormFile> Images { get; init; } = [];

}
