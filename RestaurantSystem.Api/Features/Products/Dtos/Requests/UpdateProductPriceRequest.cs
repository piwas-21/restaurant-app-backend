namespace RestaurantSystem.Api.Features.Products.Dtos.Requests;

/// <summary>Body for PATCH /api/Products/{id}/price — the new base price.</summary>
public record UpdateProductPriceRequest(decimal Price);
