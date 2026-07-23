using System.Text.Json.Serialization;

namespace RestaurantSystem.Api.Features.Products.Dtos.Requests;

/// <summary>Body for PATCH /api/Products/{id}/price — the new base price.</summary>
/// <remarks>
/// <see cref="Price"/> is <c>[JsonRequired]</c> so an omitted field is a 400 rather than a silent
/// default of <c>0</c>: the validator allows a non-negative price (a free item is valid), so a
/// missing value would otherwise pass validation and quietly zero the product's price (Sonar S6964).
/// </remarks>
public record UpdateProductPriceRequest([property: JsonRequired] decimal Price);
