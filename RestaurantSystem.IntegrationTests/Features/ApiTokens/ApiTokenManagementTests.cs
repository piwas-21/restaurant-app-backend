using FluentAssertions;
using RestaurantSystem.Api.Common.Authentication;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.ApiTokens.Dtos;
using RestaurantSystem.Domain.Common.Constants;
using RestaurantSystem.IntegrationTests.Infrastructure;
using System.Net;
using System.Net.Http.Headers;

namespace RestaurantSystem.IntegrationTests.Features.ApiTokens;

// The management half of API-TOKENS-PLAN §8 — the exact contract the admin UI is built against.
[Collection("Database Lane 2")]
public class ApiTokenManagementTests : IntegrationTestBase
{
    public ApiTokenManagementTests(DatabaseFixture databaseFixture) : base(databaseFixture)
    {
    }

    private async Task<CreatedApiTokenDto> CreateAsync(string name, params string[] scopes)
    {
        AuthenticateAsAdmin();
        var response = await PostAsJsonAsync("/api/ApiTokens", new
        {
            name,
            scopes,
            expiresInDays = 30
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await ReadResponseAsync<ApiResponse<CreatedApiTokenDto>>(response);
        return body!.Data!;
    }

    [Fact]
    public async Task Create_ReturnsThePlaintextOnceWithItsPrefixAndScopes()
    {
        var created = await CreateAsync($"seeder-{Guid.NewGuid():N}", ApiTokenScopes.MenuWrite);

        created.Token.Should().StartWith(ApiTokenDefaults.TokenPrefix);
        created.Prefix.Should().Be(created.Token[..ApiTokenHasher.PrefixLength]);
        created.Scopes.Should().Equal(ApiTokenScopes.MenuWrite);
        created.ExpiresAt.Should().BeAfter(DateTime.UtcNow);
    }

    [Fact]
    public async Task List_NeverCarriesAPlaintextAndDerivesStatus()
    {
        var created = await CreateAsync($"listed-{Guid.NewGuid():N}", ApiTokenScopes.MenuRead);

        var body = await GetFromJsonAsync<ApiResponse<List<ApiTokenDto>>>("/api/ApiTokens");
        var listed = body!.Data!.Single(t => t.Id == created.Id);

        listed.Status.Should().Be("active");
        listed.Prefix.Should().Be(created.Prefix);
        // The DTO has no Token property at all — the plaintext cannot be re-read by design.
        typeof(ApiTokenDto).GetProperty("Token").Should().BeNull();
    }

    [Fact]
    public async Task Revoke_IsIdempotentAndKillsTheTokenImmediately()
    {
        var created = await CreateAsync($"revoked-{Guid.NewGuid():N}", ApiTokenScopes.OrdersRead);

        AuthenticateAsAdmin();
        (await Client.DeleteAsync($"/api/ApiTokens/{created.Id}"))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        // Second revoke: still 200. An emergency action must not fail on a double click.
        (await Client.DeleteAsync($"/api/ApiTokens/{created.Id}"))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        Client.DefaultRequestHeaders.Remove("X-Test-Admin");
        Client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", created.Token);

        // Orders, not a menu read: the menu GETs are [AllowAnonymous], so a dead token there
        // would still be answered — as a guest — and prove nothing about revocation.
        (await Client.GetAsync("/api/Orders"))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Create_RejectsAnUnknownScope()
    {
        AuthenticateAsAdmin();
        var response = await PostAsJsonAsync("/api/ApiTokens", new
        {
            name = "typo",
            scopes = new[] { "menu:writ" },
            expiresInDays = 30
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Create_RejectsAnExpiryOutsideTheAllowedRange()
    {
        AuthenticateAsAdmin();
        var response = await PostAsJsonAsync("/api/ApiTokens", new
        {
            name = "forever",
            scopes = new[] { ApiTokenScopes.MenuRead },
            expiresInDays = 0
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Create_RejectsABodyThatOmitsTheExpiry()
    {
        // [JsonRequired] on a non-nullable int: without it the omission would bind to 0 and the
        // caller would be told "expiry must be between 1 and 365" about a value they never sent.
        AuthenticateAsAdmin();
        var response = await PostAsJsonAsync("/api/ApiTokens", new
        {
            name = "no-expiry",
            scopes = new[] { ApiTokenScopes.MenuRead }
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task NonAdmin_CannotListTokens()
    {
        AuthenticateAsUser();

        (await Client.GetAsync("/api/ApiTokens"))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
