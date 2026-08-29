using Microsoft.Extensions.DependencyInjection;
using RestaurantSystem.Api.Common.Authentication;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Infrastructure;
using System.Net.Http.Headers;

namespace RestaurantSystem.IntegrationTests.Features.ApiTokens;

/// <summary>
/// Shared machinery for the API-token tests: put a token row in the database, then send real
/// HTTP as that token through the shipped <c>ApiTokenAuthenticationHandler</c>.
/// </summary>
/// <remarks>
/// No <c>[Collection]</c> here on purpose — <see cref="IntegrationTestBase"/> explains why an
/// attribute on a base class collapses every subclass into one serial lane. Each concrete class
/// declares its own, which is what lets the tenant:write tests run in the lane that already
/// mutates the RestaurantInfo singleton instead of the one asserting its seeded values.
/// </remarks>
public abstract class ApiTokenScopeTestBase : IntegrationTestBase
{
    protected ApiTokenScopeTestBase(DatabaseFixture databaseFixture) : base(databaseFixture)
    {
    }

    /// <summary>
    /// Seeds a token row directly and returns its plaintext. Direct DB insertion is what lets a
    /// test place a token in the past — the create endpoint refuses an expiry it would not issue.
    /// </summary>
    protected async Task<string> SeedTokenAsync(
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

    /// <summary>
    /// Sends every following request as the machine token. The test-auth headers are cleared
    /// first: leaving <c>X-Test-Admin</c> in place would authenticate the caller as a human
    /// admin, and every scope assertion would then pass while proving nothing.
    /// </summary>
    protected void AuthenticateWithToken(string plaintext)
    {
        Client.DefaultRequestHeaders.Remove("X-Test-Admin");
        Client.DefaultRequestHeaders.Remove(TestAuthHandlerHeaders.Role);
        Client.DefaultRequestHeaders.Remove(TestAuthHandlerHeaders.Anonymous);
        Client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", plaintext);
    }
}
