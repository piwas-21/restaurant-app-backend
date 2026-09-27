using System.Text.Json.Serialization;

namespace RestaurantSystem.Api.Features.Catalogue.Dtos;

public sealed record ImportCatalogueSessionRequest
{
    [JsonRequired]
    public int ExpectedVersion { get; init; }
    public string IdempotencyKey { get; init; } = string.Empty;
}

public sealed record CatalogueImportResultDto(
    Guid SessionId,
    int Version,
    string Status,
    IReadOnlyList<CatalogueImportItemResultDto> Items);

public sealed record CatalogueImportItemResultDto(
    string TemplateId,
    int Revision,
    string Status,
    string? LocalEntityType,
    Guid? LocalEntityId,
    string? FailureCode);
