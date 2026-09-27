using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;

namespace RestaurantSystem.Api.Features.Catalogue.Services;

public sealed class CentralCatalogueClient(
    HttpClient httpClient,
    IOptions<CentralCatalogueSettings> settings,
    ILogger<CentralCatalogueClient> logger) : ICentralCatalogueClient
{
    private const int MaximumResponseBytes = CatalogueCurrentRevisionBatchLimits.MaximumSingleRevisionBytes;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly JsonElement UnavailableBody = JsonDocument
        .Parse("{\"error\":\"catalogue_unavailable\"}")
        .RootElement.Clone();

    public Task<CatalogueProxyResponse> GetPageAsync(
        CataloguePageQuery query,
        CancellationToken cancellationToken)
    {
        var parameters = new Dictionary<string, string?>
        {
            ["type"] = query.Type,
            ["cuisine"] = query.Cuisine,
            ["q"] = query.Query,
            ["locale"] = query.Locale,
            ["limit"] = query.Limit.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["cursor"] = query.Cursor
        };
        return SendAsync("/api/v1/catalogue/templates", parameters,
            body => CataloguePublicResponseProjector.TryProjectPage(body, out var page) ? page : null,
            cancellationToken);
    }

    public Task<CatalogueProxyResponse> GetRevisionAsync(
        string templateId,
        int revision,
        CancellationToken cancellationToken)
    {
        var path = $"/api/v1/catalogue/templates/{Uri.EscapeDataString(templateId)}/revisions/{revision}";
        return SendAsync(path, null,
            body => CataloguePublicResponseProjector.TryProjectRevision(body, templateId, revision, out var projected)
                ? projected
                : null,
            cancellationToken);
    }

    public async Task<CatalogueProxyResponse> GetCurrentRevisionBatchAsync(
        IReadOnlyList<CatalogueCurrentRevisionRequest> items,
        CancellationToken cancellationToken)
    {
        if (!CatalogueCurrentRevisionBatchRequestValidator.IsValid(items) ||
            !TryGetBaseUri(settings.Value.ApiBaseUrl, out var baseUri))
        {
            return Unavailable();
        }

        var endpoint = new Uri(baseUri, "/api/v1/catalogue/templates/current-batch");
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
            {
                Content = JsonContent.Create(new { items }, options: JsonOptions)
            };
            using var response = await httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            var body = await ReadBodyAsync(
                response,
                cancellationToken,
                CatalogueCurrentRevisionBatchLimits.MaximumBatchResponseBytes);

            if ((int)response.StatusCode == StatusCodes.Status200OK)
            {
                if (body is null || !CatalogueCurrentRevisionBatchValidator.IsValidResponse(body.Value, items))
                {
                    logger.LogWarning("Central catalogue returned an invalid batch response shape");
                    return Unavailable();
                }

                return new CatalogueProxyResponse(StatusCodes.Status200OK, body.Value);
            }

            if (response.StatusCode is HttpStatusCode.NotFound or (HttpStatusCode)429 or
                HttpStatusCode.ServiceUnavailable)
            {
                return new CatalogueProxyResponse((int)response.StatusCode, body ?? ErrorBody("catalogue_unavailable"));
            }

            logger.LogWarning("Central catalogue batch request returned status {StatusCode}", (int)response.StatusCode);
            return Unavailable();
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            logger.LogWarning(exception, "Central catalogue batch request failed with {FailureType}", exception.GetType().Name);
            return Unavailable();
        }
    }

    private async Task<CatalogueProxyResponse> SendAsync(
        string path,
        IReadOnlyDictionary<string, string?>? query,
        Func<JsonElement, JsonElement?> projectSuccessBody,
        CancellationToken cancellationToken)
    {
        if (!TryGetBaseUri(settings.Value.ApiBaseUrl, out var baseUri))
        {
            return Unavailable();
        }

        var uriBuilder = new UriBuilder(baseUri) { Path = path };
        if (query is not null)
        {
            var queryString = QueryString.Create(query.Where(pair => pair.Value is not null)
                .Select(pair => new KeyValuePair<string, string?>(pair.Key, pair.Value)));
            uriBuilder.Query = queryString.Value?.TrimStart('?') ?? string.Empty;
        }

        try
        {
            using var response = await httpClient.GetAsync(
                uriBuilder.Uri,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            var body = await ReadBodyAsync(response, cancellationToken);

            if ((int)response.StatusCode == StatusCodes.Status200OK)
            {
                var projectedBody = body is null ? null : projectSuccessBody(body.Value);
                if (projectedBody is null)
                {
                    logger.LogWarning("Central catalogue returned an invalid response shape");
                    return Unavailable();
                }

                return new CatalogueProxyResponse(StatusCodes.Status200OK, projectedBody.Value);
            }

            if (response.StatusCode is HttpStatusCode.NotFound or (HttpStatusCode)429 or
                HttpStatusCode.ServiceUnavailable)
            {
                return new CatalogueProxyResponse((int)response.StatusCode, body ?? ErrorBody("catalogue_unavailable"));
            }

            logger.LogWarning("Central catalogue request returned status {StatusCode}", (int)response.StatusCode);
            return Unavailable();
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            logger.LogWarning(exception, "Central catalogue request failed with {FailureType}", exception.GetType().Name);
            return Unavailable();
        }
    }

    private static async Task<JsonElement?> ReadBodyAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken,
        int maximumBytes = MaximumResponseBytes)
    {
        if (response.Content.Headers.ContentLength is { } contentLength && contentLength > maximumBytes)
        {
            return null;
        }

        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var destination = new MemoryStream();
        var buffer = new byte[8192];
        var total = 0;
        while (true)
        {
            var read = await source.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                break;
            }

            total += read;
            if (total > maximumBytes)
            {
                return null;
            }

            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }

        using var document = JsonDocument.Parse(destination.ToArray());
        return document.RootElement.Clone();
    }

    private static bool TryGetBaseUri(string? configured, out Uri baseUri)
    {
        baseUri = null!;
        if (!Uri.TryCreate(configured, UriKind.Absolute, out var candidate) ||
            candidate.UserInfo.Length > 0 || candidate.Query.Length > 0 || candidate.Fragment.Length > 0 ||
            candidate.AbsolutePath is not ("" or "/") ||
            !(candidate.Scheme == Uri.UriSchemeHttps ||
              candidate.Scheme == Uri.UriSchemeHttp && candidate.IsLoopback))
        {
            return false;
        }

        baseUri = candidate;
        return true;
    }

    private static CatalogueProxyResponse Unavailable() =>
        new(StatusCodes.Status503ServiceUnavailable, UnavailableBody);

    private static JsonElement ErrorBody(string code)
    {
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(new { error = code }));
        return document.RootElement.Clone();
    }
}
