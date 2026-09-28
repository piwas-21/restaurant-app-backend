namespace RestaurantSystem.Api.Features.Catalogue.Dtos;

public sealed record CatalogueImportPreviewDto(
    Guid SessionId,
    int Version,
    IReadOnlyList<CatalogueImportPreviewItemDto> Items);

public sealed record CatalogueImportPreviewItemDto(
    string TemplateId,
    int Revision,
    string Type,
    string DisplayName,
    bool IsSelected,
    string? Resolution,
    Guid? LocalEntityId,
    IReadOnlyList<CatalogueLocalCandidateDto> Candidates,
    IReadOnlyList<CatalogueImportIssueDto> Warnings,
    IReadOnlyList<CatalogueImportIssueDto> BlockingIssues,
    string? LocalEntityName = null);

public sealed record CatalogueImportIssueDto(string Code, string Message);

public sealed record CatalogueLocalCandidateDto(
    string EntityType,
    Guid Id,
    string Name,
    bool IsActive,
    string? CategoryName);
