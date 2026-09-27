using System.Text.Json.Serialization;

namespace RestaurantSystem.Api.Features.Catalogue.Dtos;

public sealed record CreateCatalogueImportSessionRequest
{
    public string TemplateId { get; init; } = string.Empty;
    [JsonRequired]
    public int Revision { get; init; }
    public string Locale { get; init; } = string.Empty;
    public string IdempotencyKey { get; init; } = string.Empty;
    public List<string>? SelectedTemplateIds { get; init; }
    [JsonRequired]
    public bool CreateNewCopy { get; init; }
}
