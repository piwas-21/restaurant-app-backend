namespace RestaurantSystem.Api.Features.RestaurantInfo.Dtos.Requests;

/// <summary>Multipart body of <c>PUT /api/restaurant-info/interior-image</c>.</summary>
public record UpdateRestaurantInteriorImageRequest
{
    public IFormFile? Image { get; init; }
}
