namespace RestaurantSystem.Api.Features.Products.Dtos;

/// <summary>
/// Dictionary-based DTO for multiple language descriptions
/// </summary>
public class ProductDescriptionsDto : Dictionary<string, ProductDescriptionDto>
{
    // Inherits from Dictionary<string, ProductDescriptionDto>
    // Key: Language code (e.g., "en", "tr", "de")
    // Value: ProductDescriptionDto containing name and description
}
