namespace RestaurantSystem.Api.Features.Catalogue.Services;

internal sealed record CatalogueSourceReference(string TemplateId, int Revision)
{
    public string Key => $"{TemplateId}@{Revision}";
}

internal sealed record CatalogueOptionReference(
    CatalogueSourceReference Reference,
    int SortOrder,
    bool IsDefault);

internal sealed record CatalogueOptionSetPayload(
    string Kind,
    int Minimum,
    int Maximum,
    IReadOnlyList<CatalogueOptionReference> Options);

internal sealed record CatalogueBundleSection(
    string Key,
    string Name,
    int DisplayOrder,
    int Minimum,
    int Maximum,
    IReadOnlyList<CatalogueOptionReference> Options,
    IReadOnlyDictionary<string, string> Translations);

internal sealed record CataloguePackReference(
    CatalogueSourceReference Reference,
    int SortOrder,
    bool IncludedByDefault);
