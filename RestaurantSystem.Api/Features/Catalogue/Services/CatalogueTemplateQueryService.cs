using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Catalogue.Dtos;
using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.Api.Features.Catalogue.Services;

public sealed class CatalogueTemplateQueryService(
    ICentralCatalogueClient catalogue,
    ICatalogueCuisinePreferencesService preferences,
    ICurrentUserService currentUser) : ICatalogueTemplateQueryService
{
    private const int DefaultPageLimit = 24;
    private const int MaximumPageLimit = 100;
    private const int MaximumQueryLength = 200;
    private const int MaximumCursorLength = 512;
    private static readonly TimeSpan RegexMatchTimeout = TimeSpan.FromMilliseconds(250);
    private static readonly HashSet<string> TemplateTypes =
        ["ingredient", "option-set", "item", "bundle", "category", "cuisine-pack"];
    private static readonly Regex TemplateIdPattern = new(
        "^[a-z0-9]+(?:-[a-z0-9]+)*$", RegexOptions.CultureInvariant, RegexMatchTimeout);
    private static readonly Regex LocalePattern = new(
        "^[A-Za-z]{2,3}(?:-[A-Za-z0-9]{2,8})*$", RegexOptions.CultureInvariant, RegexMatchTimeout);
    private static readonly Regex ShortTokenPattern = new(
        "^[a-z0-9-]+$", RegexOptions.CultureInvariant, RegexMatchTimeout);

    public async Task<CatalogueProxyResponse> GetPageAsync(
        string? type,
        string? cuisine,
        string? query,
        string? locale,
        int? limit,
        string? cursor,
        CancellationToken cancellationToken)
    {
        if (!IsValidFilters(type, cuisine, query, locale, limit, cursor))
        {
            return new CatalogueProxyResponse(StatusCodes.Status400BadRequest,
                JsonSerializer.SerializeToElement(new { error = "invalid_query" }));
        }

        var response = await catalogue.GetPageAsync(
            new CataloguePageQuery(type, cuisine, query, locale, limit ?? DefaultPageLimit, cursor),
            cancellationToken);
        if (response.StatusCode != StatusCodes.Status200OK || !currentUser.IsAdmin)
        {
            return response;
        }

        var saved = await preferences.GetAsync(cancellationToken);
        return response with { Body = CatalogueCuisineRanker.RankPage(response.Body, saved.Cuisines) };
    }

    public Task<CatalogueProxyResponse> GetRevisionAsync(
        string templateId,
        int revision,
        CancellationToken cancellationToken)
    {
        if (!TemplateIdPattern.IsMatch(templateId) || revision < 1)
        {
            return Task.FromResult(new CatalogueProxyResponse(StatusCodes.Status404NotFound,
                JsonSerializer.SerializeToElement(new { error = "not_found" })));
        }

        return catalogue.GetRevisionAsync(templateId, revision, cancellationToken);
    }

    private static bool IsValidFilters(
        string? type,
        string? cuisine,
        string? query,
        string? locale,
        int? limit,
        string? cursor) =>
        (type is null || TemplateTypes.Contains(type)) &&
        IsShortToken(cuisine, 64) &&
        (query is null || query.Length <= MaximumQueryLength) &&
        (locale is null || LocalePattern.IsMatch(locale)) &&
        (limit is null || limit is >= 1 and <= MaximumPageLimit) &&
        (cursor is null || cursor.Length <= MaximumCursorLength);

    private static bool IsShortToken(string? value, int maxLength) =>
        value is null || value.Length <= maxLength &&
        ShortTokenPattern.IsMatch(value);
}
