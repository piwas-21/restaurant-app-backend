namespace RestaurantSystem.Api.Features.Catalogue.Dtos;

public sealed record CentralCatalogueDependency
{
    public string TemplateId { get; init; } = string.Empty;
    public int Revision { get; init; }
    public string Role { get; init; } = string.Empty;
    public int? SortOrder { get; init; }
    public bool? IncludedByDefault { get; init; }
}

public sealed record CentralCatalogueTranslation
{
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
}
