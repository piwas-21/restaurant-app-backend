using FluentAssertions;
using RestaurantSystem.IntegrationTests.Infrastructure;
using System.Net;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

/// <summary>
/// Pins the auth posture of POST /api/orders/{orderId}/send-confirmation-email.
///
/// The endpoint is intentionally [AllowAnonymous] to support guest checkout
/// (see ADR-004). The replay-abuse surface is contained by the
/// "confirmation-email" per-IP rate-limit policy. These tests fail fast if
/// either decision is silently reverted.
/// </summary>
public class SendConfirmationEmailAuthTests : IntegrationTestBase
{
    public SendConfirmationEmailAuthTests(DatabaseFixture databaseFixture)
        : base(databaseFixture)
    {
    }

    /// <summary>
    /// Anonymous callers MUST NOT be challenged for credentials. The checkout
    /// flow has no JWT at this point — a 401/403 would break guest checkout.
    /// </summary>
    [Fact]
    public async Task SendConfirmationEmail_AnonymousCaller_IsNotChallengedForAuth()
    {
        // Guarantee a no-auth request: clear the admin escape hatch AND any
        // Authorization header. We don't want a stray bearer to mask a
        // regression where the endpoint silently demands credentials.
        Client.DefaultRequestHeaders.Remove("X-Test-Admin");
        Client.DefaultRequestHeaders.Authorization = null;

        var unknownOrderId = Guid.NewGuid();

        var response = await Client.PostAsync(
            $"/api/orders/{unknownOrderId}/send-confirmation-email",
            content: null);

        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized,
            "the endpoint is [AllowAnonymous] for guest-checkout support (ADR-004)");
        response.StatusCode.Should().NotBe(HttpStatusCode.Forbidden,
            "the endpoint is [AllowAnonymous] for guest-checkout support (ADR-004)");
    }

    /// <summary>
    /// The per-IP rate limit ("confirmation-email" policy) MUST be enforced.
    /// Removing [EnableRateLimiting("confirmation-email")] would re-open the
    /// SMTP-cost abuse surface ADR-004 mitigates. appsettings.Test.json pins
    /// ConfirmationEmailPermitLimit=3 so this assertion is environment-
    /// independent (Development's prod-eclipsing 1000 limit would otherwise
    /// mask any regression).
    /// </summary>
    [Fact]
    public async Task SendConfirmationEmail_ExceedingPolicy_Returns429()
    {
        Client.DefaultRequestHeaders.Remove("X-Test-Admin");
        Client.DefaultRequestHeaders.Authorization = null;
        var unknownOrderId = Guid.NewGuid();

        // Test config sets the permit limit to 3. Burst slightly past it from
        // the same loopback client (single partition) and assert at least one
        // response is 429.
        var sawThrottle = false;
        for (var i = 0; i < 5; i++)
        {
            var response = await Client.PostAsync(
                $"/api/orders/{unknownOrderId}/send-confirmation-email",
                content: null);
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                sawThrottle = true;
                break;
            }
        }

        sawThrottle.Should().BeTrue(
            "the \"confirmation-email\" rate-limit policy must block bursts past the configured permit limit");
    }
}
