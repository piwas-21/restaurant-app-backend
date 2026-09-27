namespace RestaurantSystem.Api.Features.Catalogue.Dtos;

public sealed record CatalogueImportSessionDto(
    Guid SessionId,
    string RootTemplateId,
    int RootRevision,
    string Locale,
    int Version,
    string Status,
    bool CreateNewCopy,
    IReadOnlyList<CatalogueImportSessionTemplateDto> Items);

public sealed record CatalogueImportSessionTemplateDto(
    string TemplateId,
    int Revision,
    string Type,
    string DisplayName,
    string? Description,
    string ContentHash,
    bool IsRoot,
    bool IsSelectable,
    bool IsSelected,
    string? SelectionRole,
    string Status,
    string? LocalEntityType,
    Guid? LocalEntityId,
    string? FailureCode,
    CatalogueImportItemDecision? Decision);
