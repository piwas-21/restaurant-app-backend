using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Features.Catalogue;
using RestaurantSystem.Api.Features.Catalogue.Services;

namespace RestaurantSystem.IntegrationTests.Features.Catalogue;

public sealed class CentralCatalogueClientTests
{
    [Fact]
    public async Task Missing_base_url_fails_closed_without_an_upstream_request()
    {
        var handler = new RecordingHandler((_, _) => throw new InvalidOperationException("Unexpected request"));
        using var http = new HttpClient(handler);
        var client = CreateClient(http, null);

        var response = await client.GetPageAsync(
            new CataloguePageQuery(null, null, null, null, 20, null),
            CancellationToken.None);

        response.StatusCode.Should().Be(StatusCodes.Status503ServiceUnavailable);
        response.Body.GetProperty("error").GetString().Should().Be("catalogue_unavailable");
        handler.RequestCount.Should().Be(0);
    }

    [Fact]
    public async Task Page_request_uses_fixed_central_path_and_only_supported_filters()
    {
        var handler = new RecordingHandler((request, _) =>
        {
            request.RequestUri!.AbsolutePath.Should().Be("/api/v1/catalogue/templates");
            request.RequestUri.Query.Should().Contain("type=item");
            request.RequestUri.Query.Should().Contain("cuisine=turkish");
            request.RequestUri.Query.Should().Contain("q=kofta");
            request.RequestUri.Query.Should().Contain("locale=tr");
            request.RequestUri.Query.Should().Contain("limit=12");
            request.RequestUri.Query.Should().NotContain("tenantId");
            request.Headers.Authorization.Should().BeNull();
            return Json(HttpStatusCode.OK, "{\"items\":[],\"nextCursor\":null}");
        });
        using var http = new HttpClient(handler);
        var client = CreateClient(http, CatalogueBaseUrl);

        var response = await client.GetPageAsync(
            new CataloguePageQuery("item", "turkish", "kofta", "tr", 12, null),
            CancellationToken.None);

        response.StatusCode.Should().Be(StatusCodes.Status200OK);
        response.Body.GetProperty("items").GetArrayLength().Should().Be(0);
        handler.RequestCount.Should().Be(1);
    }

    [Fact]
    public async Task Page_response_projects_only_valid_public_summary_fields()
    {
        var handler = new RecordingHandler((_, _) => Json(HttpStatusCode.OK,
            """
            {"tenantSecret":"strip","items":[{"templateId":"tr-kofte","revision":2,"type":"item",
             "cuisines":["turkish"],"displayName":"Kofte","sourceLocale":"tr","displayLocale":"en",
             "usedSourceFallback":true,"reviewedTranslationLocales":["en","tr"],"dependencyCount":3,
             "compatibleTenantContractVersions":[1],"tenantOwner":"strip"}],"nextCursor":null}
            """));
        using var http = new HttpClient(handler);
        var client = CreateClient(http, CatalogueBaseUrl);

        var response = await client.GetPageAsync(
            new CataloguePageQuery(null, null, null, "en", 24, null), CancellationToken.None);

        response.StatusCode.Should().Be(StatusCodes.Status200OK);
        response.Body.TryGetProperty("tenantSecret", out _).Should().BeFalse();
        var item = response.Body.GetProperty("items")[0];
        item.TryGetProperty("tenantOwner", out _).Should().BeFalse();
        item.GetProperty("templateId").GetString().Should().Be("tr-kofte");
    }

    [Fact]
    public async Task Malformed_public_page_item_fails_closed()
    {
        var handler = new RecordingHandler((_, _) => Json(HttpStatusCode.OK,
            """
            {"items":[{"templateId":"tr-kofte","revision":2,"type":"item"}],"nextCursor":null}
            """));
        using var http = new HttpClient(handler);
        var client = CreateClient(http, CatalogueBaseUrl);

        var response = await client.GetPageAsync(
            new CataloguePageQuery(null, null, null, "en", 24, null), CancellationToken.None);

        response.StatusCode.Should().Be(StatusCodes.Status503ServiceUnavailable);
    }

    [Fact]
    public async Task Revision_response_must_be_reviewed_and_have_an_immutable_hash()
    {
        var handler = new RecordingHandler((_, _) => Json(
            HttpStatusCode.OK,
            "{\"templateId\":\"tr-kofte\",\"revision\":1,\"qualityStatus\":\"draft\",\"contentHash\":\"abc\"}"));
        using var http = new HttpClient(handler);
        var client = CreateClient(http, CatalogueBaseUrl);

        var response = await client.GetRevisionAsync("tr-kofte", 1, CancellationToken.None);

        response.StatusCode.Should().Be(StatusCodes.Status503ServiceUnavailable);
        response.Body.GetProperty("error").GetString().Should().Be("catalogue_unavailable");
    }

    [Fact]
    public async Task Revision_response_is_identity_pinned_and_projects_only_reviewed_public_fields()
    {
        var handler = new RecordingHandler((_, _) => Json(HttpStatusCode.OK,
            """
            {"schemaVersion":1,"templateId":"tr-kofte","revision":1,"type":"category","cuisines":[],
             "name":"Kofte","description":null,"sourceLocale":"tr","translations":{"en":{"name":"Meatball",
             "description":"Food","tenantSecret":"strip"}},"localeFallbacks":["tr"],"dependencies":[],
             "provenance":{"contentOrigin":"sofra-original","sourceDescription":"Created by Sofra",
             "license":"original","mediaAssets":[],"tenantMetadata":"strip"},"qualityStatus":"reviewed",
             "compatibleTenantContractVersions":[1],"payload":{"sortOrder":1,"tenantSecret":"strip"},
             "contentHash":"immutable-hash","tenantSecret":"strip"}
            """));
        using var http = new HttpClient(handler);
        var client = CreateClient(http, CatalogueBaseUrl);

        var response = await client.GetRevisionAsync("tr-kofte", 1, CancellationToken.None);

        response.StatusCode.Should().Be(StatusCodes.Status200OK);
        response.Body.TryGetProperty("tenantSecret", out _).Should().BeFalse();
        response.Body.GetProperty("templateId").GetString().Should().Be("tr-kofte");
        response.Body.GetProperty("payload").TryGetProperty("tenantSecret", out _).Should().BeFalse();
        response.Body.GetProperty("provenance").TryGetProperty("tenantMetadata", out _).Should().BeFalse();
        response.Body.GetProperty("translations").GetProperty("en")
            .TryGetProperty("tenantSecret", out _).Should().BeFalse();
    }

    [Theory]
    [InlineData("other-template", 1)]
    [InlineData("tr-kofte", 2)]
    public async Task Revision_response_with_wrong_requested_identity_fails_closed(string returnedId, int returnedRevision)
    {
        var handler = new RecordingHandler((_, _) => Json(HttpStatusCode.OK,
            RevisionJson(returnedId, returnedRevision)));
        using var http = new HttpClient(handler);
        var client = CreateClient(http, CatalogueBaseUrl);

        var response = await client.GetRevisionAsync("tr-kofte", 1, CancellationToken.None);

        response.StatusCode.Should().Be(StatusCodes.Status503ServiceUnavailable);
    }

    [Fact]
    public async Task Revision_not_found_is_preserved_for_the_same_origin_client()
    {
        var handler = new RecordingHandler((request, _) =>
        {
            request.RequestUri!.AbsolutePath.Should().Be("/api/v1/catalogue/templates/tr-kofte/revisions/1");
            return Json(HttpStatusCode.NotFound, "{\"error\":\"not_found\"}");
        });
        using var http = new HttpClient(handler);
        var client = CreateClient(http, CatalogueBaseUrl);

        var response = await client.GetRevisionAsync("tr-kofte", 1, CancellationToken.None);

        response.StatusCode.Should().Be(StatusCodes.Status404NotFound);
        response.Body.GetProperty("error").GetString().Should().Be("not_found");
    }

    [Fact]
    public async Task Current_revision_batch_posts_only_pinned_template_ids_and_validates_complete_response()
    {
        var handler = new RecordingHandler((request, _) =>
        {
            request.Method.Should().Be(HttpMethod.Post);
            request.RequestUri!.AbsolutePath.Should().Be("/api/v1/catalogue/templates/current-batch");
            request.Headers.Authorization.Should().BeNull();
            using var requestBody = System.Text.Json.JsonDocument.Parse(
                request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
            var requestItems = requestBody.RootElement.GetProperty("items");
            requestItems.GetArrayLength().Should().Be(2);
            requestItems[0].GetProperty("templateId").GetString().Should().Be("tr-kofte");
            requestItems[0].GetProperty("adoptedRevision").GetInt32().Should().Be(1);
            requestItems[1].GetProperty("templateId").GetString().Should().Be("tr-lahmacun");
            requestItems[1].TryGetProperty("tenantId", out var tenantId).Should().BeFalse();
            tenantId.ValueKind.Should().Be(System.Text.Json.JsonValueKind.Undefined);
            return Json(HttpStatusCode.OK,
                """
                {"items":[
                  {"templateId":"tr-lahmacun","status":"notFound","revision":null,"adoptedRevisionWithdrawn":null},
                  {"templateId":"tr-kofte","status":"available","revision":{"templateId":"tr-kofte","revision":2,"qualityStatus":"reviewed","contentHash":"hash-2"},"adoptedRevisionWithdrawn":false}
                ]}
                """);
        });
        using var http = new HttpClient(handler);
        var client = CreateClient(http, CatalogueBaseUrl);

        var response = await client.GetCurrentRevisionBatchAsync(
            [new CatalogueCurrentRevisionRequest("tr-kofte", 1), new CatalogueCurrentRevisionRequest("tr-lahmacun", 3)],
            CancellationToken.None);

        response.StatusCode.Should().Be(StatusCodes.Status200OK);
        response.Body.GetProperty("items").GetArrayLength().Should().Be(2);
        handler.RequestCount.Should().Be(1);
    }

    [Theory]
    [InlineData("missing item", "{\"items\":[{\"templateId\":\"tr-kofte\",\"status\":\"notFound\",\"revision\":null,\"adoptedRevisionWithdrawn\":null}]}")]
    [InlineData("duplicate item", "{\"items\":[{\"templateId\":\"tr-kofte\",\"status\":\"notFound\",\"revision\":null,\"adoptedRevisionWithdrawn\":null},{\"templateId\":\"tr-kofte\",\"status\":\"notFound\",\"revision\":null,\"adoptedRevisionWithdrawn\":null}]}")]
    [InlineData("unknown item", "{\"items\":[{\"templateId\":\"other\",\"status\":\"notFound\",\"revision\":null,\"adoptedRevisionWithdrawn\":null},{\"templateId\":\"tr-lahmacun\",\"status\":\"notFound\",\"revision\":null,\"adoptedRevisionWithdrawn\":null}]}")]
    [InlineData("unreviewed item", "{\"items\":[{\"templateId\":\"tr-kofte\",\"status\":\"available\",\"revision\":{\"templateId\":\"tr-kofte\",\"revision\":1,\"qualityStatus\":\"draft\",\"contentHash\":\"hash\"},\"adoptedRevisionWithdrawn\":false},{\"templateId\":\"tr-lahmacun\",\"status\":\"notFound\",\"revision\":null,\"adoptedRevisionWithdrawn\":null}]}")]
    public async Task Current_revision_batch_fails_closed_on_missing_duplicate_unknown_or_unreviewed_items(string scenario, string body)
    {
        var handler = new RecordingHandler((_, _) => Json(HttpStatusCode.OK, body));
        using var http = new HttpClient(handler);
        var client = CreateClient(http, CatalogueBaseUrl);

        var response = await client.GetCurrentRevisionBatchAsync(
            [new CatalogueCurrentRevisionRequest("tr-kofte", 1), new CatalogueCurrentRevisionRequest("tr-lahmacun", 1)],
            CancellationToken.None);

        response.StatusCode.Should().Be(StatusCodes.Status503ServiceUnavailable, "the {0} batch response is invalid", scenario);
        handler.RequestCount.Should().Be(1);
    }

    [Fact]
    public async Task Current_revision_batch_rejects_duplicate_ids_before_request()
    {
        var handler = new RecordingHandler((_, _) => throw new InvalidOperationException("Unexpected request"));
        using var http = new HttpClient(handler);
        var client = CreateClient(http, CatalogueBaseUrl);

        var response = await client.GetCurrentRevisionBatchAsync(
            [new CatalogueCurrentRevisionRequest("tr-kofte", 1), new CatalogueCurrentRevisionRequest("tr-kofte", 2)],
            CancellationToken.None);

        response.StatusCode.Should().Be(StatusCodes.Status503ServiceUnavailable);
        handler.RequestCount.Should().Be(0);
    }

    [Fact]
    public async Task Current_revision_batch_rejects_more_than_the_tenant_transport_limit_before_request()
    {
        var handler = new RecordingHandler((_, _) => throw new InvalidOperationException("Unexpected request"));
        using var http = new HttpClient(handler);
        var client = CreateClient(http, CatalogueBaseUrl);
        var requests = Enumerable.Range(0, CatalogueCurrentRevisionBatchLimits.MaximumTenantBatchItems + 1)
            .Select(index => new CatalogueCurrentRevisionRequest($"template-{index}", 1))
            .ToArray();

        var response = await client.GetCurrentRevisionBatchAsync(requests, CancellationToken.None);

        response.StatusCode.Should().Be(StatusCodes.Status503ServiceUnavailable);
        handler.RequestCount.Should().Be(0);
    }

    [Theory]
    [InlineData(-1, StatusCodes.Status200OK)]
    [InlineData(1, StatusCodes.Status503ServiceUnavailable)]
    public async Task Current_revision_batch_uses_its_separate_nine_mebibyte_response_bound(
        int byteOffset,
        int expectedStatus)
    {
        var requests = Enumerable.Range(0, CatalogueCurrentRevisionBatchLimits.MaximumTenantBatchItems)
            .Select(index => new CatalogueCurrentRevisionRequest($"template-{index}", 1))
            .ToArray();
        var body = CreateBatchResponse(requests,
            CatalogueCurrentRevisionBatchLimits.MaximumBatchResponseBytes + byteOffset);
        var handler = new RecordingHandler((_, _) => Json(HttpStatusCode.OK, body));
        using var http = new HttpClient(handler);
        var client = CreateClient(http, CatalogueBaseUrl);

        var response = await client.GetCurrentRevisionBatchAsync(requests, CancellationToken.None);

        response.StatusCode.Should().Be(expectedStatus);
        handler.RequestCount.Should().Be(1);
    }

    [Fact]
    public async Task Single_revision_request_keeps_the_one_mebibyte_response_bound()
    {
        const int maximumBytes = 1_048_576;
        const string prefix = "{\"templateId\":\"tr-kofte\",\"revision\":1,\"qualityStatus\":\"reviewed\",\"contentHash\":\"hash\",\"padding\":\"";
        const string suffix = "\"}";
        var body = string.Concat(prefix, new string('x', maximumBytes - prefix.Length - suffix.Length + 1), suffix);
        var handler = new RecordingHandler((_, _) => Json(HttpStatusCode.OK, body));
        using var http = new HttpClient(handler);
        var client = CreateClient(http, CatalogueBaseUrl);

        var response = await client.GetRevisionAsync("tr-kofte", 1, CancellationToken.None);

        response.StatusCode.Should().Be(StatusCodes.Status503ServiceUnavailable);
        handler.RequestCount.Should().Be(1);
    }

    [Fact]
    public async Task Batch_rejects_an_individual_revision_over_the_single_revision_limit()
    {
        const string revisionPrefix = "{\"templateId\":\"oversized\",\"revision\":2,\"qualityStatus\":\"reviewed\",\"contentHash\":\"hash\",\"padding\":\"";
        const string revisionSuffix = "\"}";
        var paddingLength = CatalogueCurrentRevisionBatchLimits.MaximumSingleRevisionBytes + 1 -
            revisionPrefix.Length - revisionSuffix.Length;
        var revision = string.Concat(revisionPrefix, new string('x', paddingLength), revisionSuffix);
        var body = $"{{\"items\":[{{\"templateId\":\"oversized\",\"status\":\"available\",\"revision\":{revision},\"adoptedRevisionWithdrawn\":false}}]}}";
        var handler = new RecordingHandler((_, _) => Json(HttpStatusCode.OK, body));
        using var http = new HttpClient(handler);
        var client = CreateClient(http, CatalogueBaseUrl);

        var response = await client.GetCurrentRevisionBatchAsync(
            [new CatalogueCurrentRevisionRequest("oversized", 1)], CancellationToken.None);

        body.Length.Should().BeLessThan(CatalogueCurrentRevisionBatchLimits.MaximumBatchResponseBytes);
        response.StatusCode.Should().Be(StatusCodes.Status503ServiceUnavailable);
    }

    [Theory]
    [InlineData("http", "")]
    [InlineData("https", "other/path")]
    public async Task Invalid_base_urls_fail_closed(string scheme, string path)
    {
        var handler = new RecordingHandler((_, _) => throw new InvalidOperationException("Unexpected request"));
        using var http = new HttpClient(handler);
        var client = CreateClient(http, new UriBuilder(scheme, "example.test") { Path = path }.ToString());

        var response = await client.GetRevisionAsync("tr-kofte", 1, CancellationToken.None);

        response.StatusCode.Should().Be(StatusCodes.Status503ServiceUnavailable);
        handler.RequestCount.Should().Be(0);
    }

    private static CentralCatalogueClient CreateClient(HttpClient http, string? baseUrl) => new(
        http,
        Options.Create(new CentralCatalogueSettings { ApiBaseUrl = baseUrl }),
        NullLogger<CentralCatalogueClient>.Instance);

    private static string CatalogueBaseUrl => new UriBuilder(Uri.UriSchemeHttps, "catalogue.example.test").ToString();

    private static HttpResponseMessage Json(HttpStatusCode status, string body) => new(status)
    {
        Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json")
    };

    private static string CreateBatchResponse(
        IReadOnlyList<CatalogueCurrentRevisionRequest> requests,
        int totalBytes)
    {
        var items = string.Join(",", requests.Select(item =>
            $"{{\"templateId\":\"{item.TemplateId}\",\"status\":\"available\",\"revision\":{{\"templateId\":\"{item.TemplateId}\",\"revision\":2,\"qualityStatus\":\"reviewed\",\"contentHash\":\"{new string('a', 64)}\"}},\"adoptedRevisionWithdrawn\":false}}"));
        var prefix = $"{{\"items\":[{items}],\"padding\":\"";
        const string suffix = "\"}";
        var paddingLength = totalBytes - prefix.Length - suffix.Length;
        if (paddingLength < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(totalBytes));
        }

        return string.Concat(prefix, new string('x', paddingLength), suffix);
    }

    private static string RevisionJson(string templateId, int revision) => $$"""
        {"schemaVersion":1,"templateId":"{{templateId}}","revision":{{revision}},"type":"category","cuisines":[],
         "name":"Kofte","description":null,"sourceLocale":"tr","translations":{},"localeFallbacks":[],
         "dependencies":[],"provenance":{"contentOrigin":"sofra-original","sourceDescription":"Created by Sofra",
         "license":"original","mediaAssets":[]},"qualityStatus":"reviewed","compatibleTenantContractVersions":[1],
         "payload":{"sortOrder":1},"contentHash":"immutable-hash"}
        """;

    private sealed class RecordingHandler(
        Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            return Task.FromResult(responseFactory(request, cancellationToken));
        }
    }
}
