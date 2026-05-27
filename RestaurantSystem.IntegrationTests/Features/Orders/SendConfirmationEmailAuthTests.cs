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
        AuthenticateAsUser(); // clears the X-Test-Admin header; sends no Authorization either

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
    /// The per-IP rate limit ("confirmation-email" policy, 5/15min in
    /// production defaults loaded from appsettings.json) MUST be enforced.
    /// Removing [EnableRateLimiting("confirmation-email")] would re-open the
    /// SMTP-cost abuse surface ADR-004 mitigates.
    /// </summary>
    [Fact]
    public async Task SendConfirmationEmail_ExceedingPolicy_Returns429()
    {
        AuthenticateAsUser();
        var unknownOrderId = Guid.NewGuid();

        // Production default is 5 permits / 15 min / IP. Burst past it from
        // the same loopback client to trip the partitioned limiter.
        HttpResponseMessage? lastResponse = null;
        for (var i = 0; i < 10; i++)
        {
            lastResponse = await Client.PostAsync(
                $"/api/orders/{unknownOrderId}/send-confirmation-email",
                content: null);
            if (lastResponse.StatusCode == HttpStatusCode.TooManyRequests)
            {
                return; // policy enforced — test passes
            }
        }

        lastResponse!.StatusCode.Should().Be(HttpStatusCode.TooManyRequests,
            "the \"confirmation-email\" rate-limit policy must block bursts past the configured permit limit");
    }
}
