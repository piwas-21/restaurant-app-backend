using System.Globalization;
using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Auth;

/// <summary>
/// GAP-3 (EMAIL-SPEC-TENANT-APP §4) — POST /api/Auth/send-email-verification is
/// <c>[AllowAnonymous]</c> and sends a real mail per call. Ungoverned it is an email-bombing
/// primitive against any known customer address and a way to burn the tenant's mail quota.
///
/// <para>
/// Two layers, and both are asserted here because either alone is escapable: the per-IP
/// "email-verification" policy (one caller) and the per-address cooldown (one inbox, however many
/// IPs ask). appsettings.Test.json pins EmailVerificationPermitLimit=3 and
/// EmailVerificationCooldownMinutes=60 so nothing here depends on the environment's defaults.
/// </para>
///
/// <para>
/// Assertions are made against a <b>recording <c>IEmailService</c></b> — what matters is that no
/// mail left the building, not that a counter moved. And every throttled branch must still answer
/// exactly what the first call answered: a distinguishable refusal would turn the endpoint into an
/// address oracle, which is the thing its generic success sentence exists to prevent.
/// </para>
/// </summary>
public class EmailVerificationThrottleTests : IntegrationTestBase
{
    private const string Password = "Str0ng!Passw0rd"; // pragma: allowlist secret (test-only)
    private const string UnverifiedEmail = "unverified@example.test";

    private readonly Mock<IEmailService> _email = new();

    public EmailVerificationThrottleTests(DatabaseFixture databaseFixture)
        : base(databaseFixture)
    {
    }

    protected override void ConfigureTestServices(IServiceCollection services)
    {
        services.RemoveAll<IEmailService>();
        services.AddSingleton(_email.Object);
    }

    /// <summary>
    /// The per-IP layer. Removing <c>[EnableRateLimiting("email-verification")]</c> from the endpoint
    /// breaks this — and the address is deliberately one that does not exist, so the throttle is
    /// proven to apply before any user lookup, which is what stops address enumeration by volume.
    /// </summary>
    [Fact]
    public async Task SendEmailVerification_ExceedingPolicy_Returns429()
    {
        AuthenticateAsAnonymous();

        var sawThrottle = false;
        for (var i = 0; i < 6; i++)
        {
            var response = await PostAsync("nobody@example.test");
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                sawThrottle = true;
                break;
            }
        }

        sawThrottle.Should().BeTrue(
            "the \"email-verification\" policy must throttle verification-resend bursts past its permit limit");
    }

    /// <summary>
    /// The endpoint must NOT share the "forgot-password" bucket. A guest tapping "resend" on the
    /// restaurant's Wi-Fi would otherwise 429 that whole NAT out of password reset for an hour —
    /// the same failure that gave refresh-token its own partition after admins were locked out of
    /// re-login.
    /// </summary>
    [Fact]
    public async Task A_verification_burst_does_not_throttle_password_reset()
    {
        AuthenticateAsAnonymous();

        for (var i = 0; i < 8; i++)
        {
            await PostAsync("nobody@example.test");
        }

        var forgot = await Client.PostAsync(
            "/api/Auth/forgot-password",
            new StringContent("{\"email\":\"nobody@example.test\"}", Encoding.UTF8, "application/json"));

        forgot.StatusCode.Should().NotBe(HttpStatusCode.TooManyRequests,
            "verification resends and password reset must not share a rate-limit bucket");
    }

    /// <summary>
    /// The per-address layer, which is the one that matters for bombing: an attacker rotating IPs
    /// never trips the policy above, and a mail per request is the whole attack.
    /// </summary>
    [Fact]
    public async Task A_second_request_within_the_cooldown_sends_no_mail()
    {
        var user = await SeedUnverifiedCustomerAsync();
        AuthenticateAsAnonymous();

        var first = await PostAsync(user.Email!);
        var second = await PostAsync(user.Email!);

        first.StatusCode.Should().Be(HttpStatusCode.OK);
        second.StatusCode.Should().Be(HttpStatusCode.OK);
        (await second.Content.ReadAsStringAsync()).Should().Be(
            await first.Content.ReadAsStringAsync(),
            "a throttled resend must be indistinguishable from an accepted one, or it becomes an address oracle");

        VerifyVerificationMails(user.Id, Times.Once());
    }

    /// <summary>
    /// The cooldown is a delay, not a ban. A genuine user whose mail went to spam must be able to
    /// ask again — so the window has to actually expire.
    /// </summary>
    [Fact]
    public async Task A_request_after_the_cooldown_expires_sends_again()
    {
        var user = await SeedUnverifiedCustomerAsync();
        AuthenticateAsAnonymous();

        await PostAsync(user.Email!);
        await AgeTheCooldownAsync(user.Id);
        await PostAsync(user.Email!);

        VerifyVerificationMails(user.Id, Times.Exactly(2));
    }

    /// <summary>
    /// Registration sends the verification mail itself, so it must open the cooldown too —
    /// otherwise the register screen's own "resend" button delivers a second identical mail
    /// seconds after the first, and the cooldown only starts on the one nobody needed.
    /// </summary>
    [Fact]
    public async Task Registration_opens_the_cooldown_for_the_new_address()
    {
        AuthenticateAsAnonymous();
        const string email = "fresh@example.test";

        var registration = await Client.PostAsync(
            "/api/User/register/customer",
            new StringContent(
                $"{{\"firstName\":\"Fresh\",\"lastName\":\"Guest\",\"email\":\"{email}\"," +
                $"\"password\":\"{Password}\",\"confirmPassword\":\"{Password}\"}}",
                Encoding.UTF8,
                "application/json"));
        registration.StatusCode.Should().Be(HttpStatusCode.OK);

        var resend = await PostAsync(email);
        resend.StatusCode.Should().Be(HttpStatusCode.OK);

        var user = await FindUserAsync(email);
        VerifyVerificationMails(user.Id, Times.Once());
    }

    /// <summary>
    /// An address nobody registered must cost nothing and reveal nothing.
    /// </summary>
    [Fact]
    public async Task An_unknown_address_sends_nothing()
    {
        AuthenticateAsAnonymous();

        var response = await PostAsync("ghost@example.test");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        _email.Verify(
            e => e.SendEmailVerificationAsync(It.IsAny<CultureInfo>(), It.IsAny<ApplicationUser>(), It.IsAny<string>(), It.IsAny<string?>()),
            Times.Never());
    }

    /// <summary>
    /// The property that keeps the cooldown from becoming a weapon: a SUPPRESSED request must not
    /// re-stamp. Stamping unconditionally ("always record the attempt" is the plausible refactor)
    /// would let an attacker pinging every 4 minutes hold a real user's window open forever —
    /// turning an anti-bombing measure into permanent denial of verification.
    /// </summary>
    [Fact]
    public async Task A_suppressed_request_does_not_extend_the_cooldown()
    {
        var user = await SeedUnverifiedCustomerAsync();
        AuthenticateAsAnonymous();

        await PostAsync(user.Email!);
        var openedAt = await StampOf(user.Id);

        await PostAsync(user.Email!);

        (await StampOf(user.Id)).Should().Be(openedAt,
            "a suppressed attempt must leave the window expiring on its original schedule");
        VerifyVerificationMails(user.Id, Times.Once());
    }

    /// <summary>
    /// An address that is already verified must answer exactly what an unknown one answers. It
    /// used to say "Email is already verified.", which is an oracle for precisely the accounts an
    /// attacker wants to find — and it was the last branch that could be told apart.
    /// </summary>
    [Fact]
    public async Task An_already_verified_address_is_indistinguishable_from_an_unknown_one()
    {
        var user = await SeedUnverifiedCustomerAsync(emailConfirmed: true);
        AuthenticateAsAnonymous();

        var verified = await PostAsync(user.Email!);
        var unknown = await PostAsync("ghost@example.test");

        (await verified.Content.ReadAsStringAsync()).Should().Be(
            await unknown.Content.ReadAsStringAsync(),
            "the already-verified branch must not identify a registered account");
        VerifyVerificationMails(user.Id, Times.Never());
    }

    /// <summary>
    /// The deliberate trade: the stamp is taken before the send and KEPT when the send throws.
    /// Releasing it would hand the bombing vector back at exactly the moment the mail provider is
    /// unhealthy. Unlike an <c>IOutboundEmailLedger</c> claim this is time-bounded, so nothing is
    /// permanently unsendable — the next window still delivers.
    /// </summary>
    [Fact]
    public async Task A_send_that_throws_still_burns_the_cooldown()
    {
        _email.Setup(e => e.SendEmailVerificationAsync(
                It.IsAny<CultureInfo>(), It.IsAny<ApplicationUser>(), It.IsAny<string>(), It.IsAny<string?>()))
            .ThrowsAsync(new InvalidOperationException("provider down"));

        var user = await SeedUnverifiedCustomerAsync();
        AuthenticateAsAnonymous();

        var response = await PostAsync(user.Email!);

        response.StatusCode.Should().Be(HttpStatusCode.OK, "a failed send must not be visible to the caller");
        (await StampOf(user.Id)).Should().NotBeNull("the attempt is what the cooldown counts");

        await PostAsync(user.Email!);
        VerifyVerificationMails(user.Id, Times.Once(), "the second attempt is inside the window it opened");
    }

    private Task<HttpResponseMessage> PostAsync(string email) =>
        Client.PostAsync(
            "/api/Auth/send-email-verification",
            new StringContent($"{{\"email\":\"{email}\"}}", Encoding.UTF8, "application/json"));

    private void VerifyVerificationMails(Guid userId, Times times, string because = "") =>
        _email.Verify(
            e => e.SendEmailVerificationAsync(
                It.IsAny<CultureInfo>(), It.Is<ApplicationUser>(u => u.Id == userId), It.IsAny<string>(), It.IsAny<string?>()),
            times,
            because);

    private async Task<DateTime?> StampOf(Guid userId)
    {
        using var scope = Factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.FindByIdAsync(userId.ToString());
        return user!.LastEmailVerificationSentAt;
    }

    private async Task<ApplicationUser> SeedUnverifiedCustomerAsync(bool emailConfirmed = false)
    {
        using var scope = Factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        var user = new ApplicationUser
        {
            UserName = UnverifiedEmail,
            Email = UnverifiedEmail,
            EmailConfirmed = emailConfirmed,
            FirstName = "Unverified",
            LastName = "Guest",
            Role = UserRole.Customer,
            CreatedBy = "test",
            RefreshToken = string.Empty,
        };

        var created = await userManager.CreateAsync(user, Password);
        created.Succeeded.Should().BeTrue(string.Join(", ", created.Errors.Select(e => e.Description)));
        return user;
    }

    /// <summary>Backdates the stamp past the configured window — the only way to test expiry
    /// without sleeping for it.</summary>
    private async Task AgeTheCooldownAsync(Guid userId)
    {
        using var scope = Factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.FindByIdAsync(userId.ToString());
        user!.LastEmailVerificationSentAt = DateTime.UtcNow.AddDays(-1);
        (await userManager.UpdateAsync(user)).Succeeded.Should().BeTrue();
    }

    private async Task<ApplicationUser> FindUserAsync(string email)
    {
        using var scope = Factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.FindByEmailAsync(email);
        user.Should().NotBeNull();
        return user!;
    }
}
