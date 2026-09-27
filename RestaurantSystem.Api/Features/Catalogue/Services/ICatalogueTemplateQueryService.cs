namespace RestaurantSystem.Api.Features.Catalogue.Services;

public interface ICatalogueTemplateQueryService
{
    Task<CatalogueProxyResponse> GetPageAsync(
        string? type,
        string? cuisine,
        string? query,
        string? locale,
        int? limit,
        string? cursor,
        CancellationToken cancellationToken);

    Task<CatalogueProxyResponse> GetRevisionAsync(
        string templateId,
        int revision,
        CancellationToken cancellationToken);
}
