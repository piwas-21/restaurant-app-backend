using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RestaurantSystem.Api.Common.Authentication;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.ApiTokens.Dtos;
using RestaurantSystem.Domain.Common.Constants;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Infrastructure;
using System.Net;
using System.Net.Http.Headers;

namespace RestaurantSystem.IntegrationTests.Features.ApiTokens;

// The authentication half of API-TOKENS-PLAN: what a machine token can and cannot do.
// Real HTTP through the real ApiTokenAuthenticationHandler — TestAuthHandler forwards any
// `Bearer sk_...` to it rather than faking a principal, so these assertions are about the
// shipped code path and not about the test harness.
[Collection("Database Lane 2")]
public class ApiTokenAuthenticationTests : IntegrationTestBase
{
    public ApiTokenAuthenticationTests(DatabaseFixture databaseFixture) : base(databaseFixture)
    {
    }

    /// <summary>
    /// Seeds a token row directly and returns its plaintext. Direct DB insertion is what lets a
    /// test place a token in the past — the create endpoint refuses an expiry it would not issue.
    /// </summary>
    private async Task<string> SeedTokenAsync(
        IEnumerable<string> scopes, DateTime? expiresAt = null, DateTime? revokedAt = null)
    {
        var plaintext = ApiTokenHasher.GenerateToken();

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        db.Set<ApiToken>().Add(new ApiToken
        {
            Id = Guid.NewGuid(),
            Name = $"test-{Guid.NewGuid():N}",
            TokenHash = ApiTokenHasher.ComputeHash(plaintext),
            Prefix = ApiTokenHasher.ExtractPrefix(plaintext),
            Scopes = scopes.ToList(),
            ExpiresAt = expiresAt ?? DateTime.UtcNow.AddDays(7),
            RevokedAt = revokedAt,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test"
        });

        await db.SaveChangesAsync();
        return plaintext;
    }

    private void AuthenticateWithToken(string plaintext)
    {
        Client.DefaultRequestHeaders.Remove("X-Test-Admin");
        Client.DefaultRequestHeaders.Remove(TestAuthHandlerHeaders.Role);
        Client.DefaultRequestHeaders.Remove(TestAuthHandlerHeaders.Anonymous);
        Client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", plaintext);
    }

    [Fact]
    public async Task ValidScopedToken_ReachesTheEndpointInItsScope()
    {
        // /api/Orders, not /api/Categories: the menu reads are [AllowAnonymous], so a token
        // succeeding there would prove only that the endpoint is public. Orders requires an
        // authenticated caller, so a 200 here is the token doing the work.
        AuthenticateWithToken(await SeedTokenAsync([ApiTokenScopes.OrdersRead]));

        var response = await Client.GetAsync("/api/Orders");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ReadScopedToken_CannotWrite()
    {
        // The authorization filter runs before model binding, so the empty body never matters:
        // a menu:read token is refused the write endpoint outright.
        AuthenticateWithToken(await SeedTokenAsync([ApiTokenScopes.MenuRead]));

        var response = await PostAsJsonAsync("/api/Categories", new { });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ExpiredToken_IsRejectedWith401()
    {
        AuthenticateWithToken(await SeedTokenAsync(
            [ApiTokenScopes.MenuRead], expiresAt: DateTime.UtcNow.AddMinutes(-1)));

        var response = await Client.GetAsync("/api/Orders");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task RevokedToken_IsRejectedWith401()
    {
        AuthenticateWithToken(await SeedTokenAsync(
            [ApiTokenScopes.MenuRead], revokedAt: DateTime.UtcNow.AddMinutes(-1)));

        var response = await Client.GetAsync("/api/Orders");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task UnknownToken_IsRejectedWith401()
    {
        AuthenticateWithToken(ApiTokenHasher.GenerateToken());

        var response = await Client.GetAsync("/api/Orders");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task TokenMissingTheScope_Gets403WithMissingScopeCode()
    {
        // A read-only menu token asking for orders: the credential is valid, the permission
        // is not. 403 and not 401, so a client can tell "re-issue me" from "widen me".
        AuthenticateWithToken(await SeedTokenAsync([ApiTokenScopes.MenuRead]));

        var response = await Client.GetAsync("/api/Orders");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var body = await ReadResponseAsync<ApiResponse<object>>(response);
        body!.ErrorCode.Should().Be(ErrorCodes.MissingScope);
    }

    [Fact]
    public async Task Token_CannotListTokens_EvenWithEveryScope()
    {
        // The whole vocabulary, and still refused: /api/ApiTokens carries no [ApiScope], and
        // absence is a denial. This is the property that stops a leaked token minting itself
        // a successor.
        AuthenticateWithToken(await SeedTokenAsync(ApiTokenScopes.All));

        var response = await Client.GetAsync("/api/ApiTokens");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Token_CannotCreateTokens_EvenWithEveryScope()
    {
        AuthenticateWithToken(await SeedTokenAsync(ApiTokenScopes.All));

        var response = await PostAsJsonAsync("/api/ApiTokens", new
        {
            name = "escalation",
            scopes = new[] { ApiTokenScopes.MenuWrite },
            expiresInDays = 30
        });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task SuccessfulUse_StampsLastUsedAt()
    {
        var plaintext = await SeedTokenAsync([ApiTokenScopes.OrdersRead]);
        AuthenticateWithToken(plaintext);

        (await Client.GetAsync("/api/Orders")).EnsureSuccessStatusCode();

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var hash = ApiTokenHasher.ComputeHash(plaintext);

        var stored = await db.Set<ApiToken>().AsNoTracking()
            .FirstAsync(t => t.TokenHash == hash);

        stored.LastUsedAt.Should().NotBeNull();
    }
}
