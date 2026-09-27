using System.Text.Json;

namespace RestaurantSystem.Api.Features.Catalogue.Dtos;

public sealed record CentralCatalogueTemplateRevision
{
    public int SchemaVersion { get; init; }
    public string TemplateId { get; init; } = string.Empty;
    public int Revision { get; init; }
    public string Type { get; init; } = string.Empty;
    public List<string> Cuisines { get; init; } = [];
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public string SourceLocale { get; init; } = string.Empty;
    public Dictionary<string, CentralCatalogueTranslation> Translations { get; init; } = [];
    public List<string> LocaleFallbacks { get; init; } = [];
    public List<CentralCatalogueDependency> Dependencies { get; init; } = [];
    public JsonElement Provenance { get; init; }
    public string QualityStatus { get; init; } = string.Empty;
    public List<int> CompatibleTenantContractVersions { get; init; } = [];
    public JsonElement Payload { get; init; }
    public string ContentHash { get; init; } = string.Empty;
}
