namespace RestaurantSystem.Api.Features.RestaurantInfo.Dtos.Requests;

/// <summary>Multipart body of <c>PUT /api/restaurant-info/logo/{variant}</c>.</summary>
public record UpdateRestaurantLogoRequest
{
    public IFormFile? Logo { get; init; }
}
