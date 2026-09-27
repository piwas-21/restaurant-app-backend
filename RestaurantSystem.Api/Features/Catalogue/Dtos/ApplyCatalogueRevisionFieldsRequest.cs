namespace RestaurantSystem.Api.Features.Catalogue.Dtos;

public sealed record ApplyCatalogueRevisionFieldsRequest
{
    public required int ExpectedSessionVersion { get; init; }
    public string TemplateId { get; init; } = string.Empty;
    public required int AdoptedRevision { get; init; }
    public required int CurrentRevision { get; init; }
    public string CurrentContentHash { get; init; } = string.Empty;
    public string ExpectedLocalHash { get; init; } = string.Empty;
    public List<string> FieldPaths { get; init; } = [];
}

public sealed record CatalogueRevisionFieldApplyResultDto(
    Guid SessionId,
    int SessionVersion,
    string TemplateId,
    int AdoptedRevision,
    string ContentHash,
    IReadOnlyList<string> AppliedFieldPaths,
    string LocalHash);
