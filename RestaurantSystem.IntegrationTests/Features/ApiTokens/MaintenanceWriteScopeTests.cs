using System.Net;
using System.Net.Http.Headers;
using FluentAssertions;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Domain.Common.Constants;
using RestaurantSystem.IntegrationTests.Common;
using RestaurantSystem.IntegrationTests.Features.ApiTokens;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.ApiTokens;

/// <summary>
/// API-TOKENS-PLAN §2 — the <c>maintenance:write</c> scope (2026-09-07): a machine client may run
/// the admin image-maintenance jobs. Same pair discipline as <see cref="TenantWriteScopeTests"/>:
/// the positive half proves the annotation landed, the negative half proves the scope
/// discriminates rather than the endpoint being open to every token.
/// </summary>
/// <remarks>
/// Dry-run only (apply defaults to false): the assertion target is AUTHORIZATION, and a dry run
/// writes nothing — the backfill walk itself is covered by its own suites. The host carries
/// <c>FileStorage:Provider=Local</c> so the positive case passes the service's provider contract
/// instead of stopping at it.
/// </remarks>
[Collection("Database Lane 2")]
public class MaintenanceWriteScopeTests : ApiTokenScopeTestBase
{
    private const string CardVariantsUrl = "/api/maintenance/images/card-variants";

    private readonly DatabaseFixture _databaseFixture;

    public MaintenanceWriteScopeTests(DatabaseFixture databaseFixture) : base(databaseFixture)
    {
        _databaseFixture = databaseFixture;
    }

    private HttpClient CreateLocalProviderClient(string? plaintext)
    {
        var factory = new TestWebApplicationFactory(_databaseFixture.ConnectionString, new Dictionary<string, string>
        {
            ["FileStorage:Provider"] = "Local",
            ["LocalStorage:BaseUrl"] = "https://uploads.example.test",
        });
        var client = factory.CreateClient();
        if (plaintext is null)
        {
            client.DefaultRequestHeaders.Add(TestAuthHandler.AnonymousHeader, "true");
        }
        else
        {
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", plaintext);
        }
        return client;
    }

    [Fact]
    public async Task MaintenanceWriteToken_ReachesTheCardVariantDryRun()
    {
        var plaintext = await SeedTokenAsync([ApiTokenScopes.MaintenanceWrite]);
        using var client = CreateLocalProviderClient(plaintext);

        var response = await client.PostAsync(CardVariantsUrl + "?apply=false&maxRows=5", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task TokenWithoutTheScope_IsRefusedTheCardVariantDryRun()
    {
        var plaintext = await SeedTokenAsync([ApiTokenScopes.MenuRead]);
        using var client = CreateLocalProviderClient(plaintext);

        var response = await client.PostAsync(CardVariantsUrl + "?apply=false&maxRows=5", null);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var body = await ReadResponseAsync<ApiResponse<object>>(response);
        body!.ErrorCode.Should().Be(ErrorCodes.MissingScope);
    }

    [Fact]
    public async Task AnonymousCaller_IsRefusedTheCardVariantDryRun()
    {
        using var client = CreateLocalProviderClient(null);

        var response = await client.PostAsync(CardVariantsUrl + "?apply=false&maxRows=5", null);

        response.StatusCode.Should().BeOneOf(HttpStatusCode.Forbidden, HttpStatusCode.Unauthorized);
    }
}
