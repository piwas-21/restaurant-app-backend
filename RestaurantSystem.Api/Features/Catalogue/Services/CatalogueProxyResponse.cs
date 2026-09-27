using System.Text.Json;

namespace RestaurantSystem.Api.Features.Catalogue.Services;

public sealed record CatalogueProxyResponse(int StatusCode, JsonElement Body);
