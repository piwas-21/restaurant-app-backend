using System.Net;
using System.Text;
using FluentAssertions;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Auth;

/// <summary>
/// Pins the rate-limit posture of POST /api/Auth/refresh-token.
///
/// The endpoint has its OWN per-IP policy ("auth-refresh"), separate from the
/// "auth" policy that guards /login. This is the regression guard for the
/// production bug where a mid-session token-refresh stampede drained the shared
/// login bucket and 429'd admins out of re-login. appsettings.Test.json pins
/// AuthRefreshPermitLimit=3 (and AuthPermitLimit=3) so these assertions are
/// environment-independent.
/// </summary>
public class RefreshTokenRateLimitTests : IntegrationTestBase
{
    public RefreshTokenRateLimitTests(DatabaseFixture databaseFixture)
        : base(databaseFixture)
    {
    }

    private static StringContent RefreshBody() =>
        new("{\"accessToken\":\"expired\",\"refreshToken\":\"stale\"}", Encoding.UTF8, "application/json");

    private static StringContent LoginBody() =>
        new("{\"email\":\"nobody@example.test\",\"password\":\"wrong\"}", Encoding.UTF8, "application/json");

    /// <summary>
    /// The "auth-refresh" policy MUST be enforced. Removing
    /// [EnableRateLimiting("auth-refresh")] (or failing to register the policy)
    /// would re-open the abuse surface and break this assertion.
    /// </summary>
    [Fact]
    public async Task RefreshToken_ExceedingPolicy_Returns429()
    {
        Client.DefaultRequestHeaders.Remove("X-Test-Admin");

        var sawThrottle = false;
        for (var i = 0; i < 6; i++)
        {
            var response = await Client.PostAsync("/api/Auth/refresh-token", RefreshBody());
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                sawThrottle = true;
                break;
            }
        }

        sawThrottle.Should().BeTrue(
            "the \"auth-refresh\" policy must throttle refresh bursts past the configured permit limit");
    }

    /// <summary>
    /// The core regression guard: a refresh-token burst MUST NOT consume the
    /// login bucket. Before the fix, refresh shared /login's "auth" partition,
    /// so a refresh storm 429'd the admin's subsequent re-login attempt.
    /// </summary>
    [Fact]
    public async Task RefreshTokenBurst_DoesNotThrottleLogin()
    {
        Client.DefaultRequestHeaders.Remove("X-Test-Admin");

        // Drain the refresh bucket well past its permit limit (3).
        for (var i = 0; i < 8; i++)
        {
            await Client.PostAsync("/api/Auth/refresh-token", RefreshBody());
        }

        // Login lives in a DIFFERENT partition — a single attempt must NOT be
        // rate-limited (it may fail auth with 401, but never 429).
        var loginResponse = await Client.PostAsync("/api/Auth/login", LoginBody());

        loginResponse.StatusCode.Should().NotBe(HttpStatusCode.TooManyRequests,
            "refresh-token and login must use separate rate-limit buckets");
    }
}
