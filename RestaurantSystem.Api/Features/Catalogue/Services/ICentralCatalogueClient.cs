namespace RestaurantSystem.Api.Features.Catalogue.Services;

public interface ICentralCatalogueClient
{
    Task<CatalogueProxyResponse> GetPageAsync(
        CataloguePageQuery query,
        CancellationToken cancellationToken);

    Task<CatalogueProxyResponse> GetRevisionAsync(
        string templateId,
        int revision,
        CancellationToken cancellationToken);

    Task<CatalogueProxyResponse> GetCurrentRevisionBatchAsync(
        IReadOnlyList<CatalogueCurrentRevisionRequest> items,
        CancellationToken cancellationToken);
}

public sealed record CatalogueCurrentRevisionRequest(string TemplateId, int AdoptedRevision);

public static class CatalogueCurrentRevisionBatchLimits
{
    public const int MaximumItems = 128;
    public const int MaximumTenantBatchItems = 8;
    public const int MaximumSingleRevisionBytes = 1_048_576;
    public const int MaximumBatchResponseBytes = 9 * MaximumSingleRevisionBytes;
    public const int MaximumTemplateIdLength = 120;
}

public sealed record CataloguePageQuery(
    string? Type,
    string? Cuisine,
    string? Query,
    string? Locale,
    int Limit,
    string? Cursor);
