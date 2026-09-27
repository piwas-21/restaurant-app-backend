namespace RestaurantSystem.Api.Features.Catalogue.Dtos;

public sealed record CatalogueRevisionChangesDto(
    Guid SessionId,
    int SessionVersion,
    IReadOnlyList<CatalogueRevisionChangeItemDto> Items);

public sealed record CatalogueRevisionChangeItemDto(
    string TemplateId,
    int AdoptedRevision,
    string AdoptedContentHash,
    int? CurrentRevision,
    string? CurrentContentHash,
    bool Withdrawn,
    bool? AdoptedRevisionWithdrawn,
    string Status,
    IReadOnlyList<CatalogueRevisionFieldChangeDto> Fields,
    string? Notice,
    string? LocalHash);

public sealed record CatalogueRevisionFieldChangeDto(
    string Path,
    string? Baseline,
    string? Current,
    string? LocalValue,
    bool LocalChanged);
