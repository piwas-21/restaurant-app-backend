using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Auth.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Common;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Auth;

/// <summary>
/// The refresh-session model: every login issues its OWN rotating refresh credential
/// (<c>RefreshSessions</c>), a credential is single-use (rotation), the pre-migration
/// single-hash credential on <c>ApplicationUser</c> is accepted exactly once and then
/// migrated, and a password change revokes everything.
///
/// The property that matters most is rotation: whoever steals an issued refresh token must not
/// be able to keep refreshing after the legitimate client used it once. The legacy bridge is
/// what makes the migration deployable without logging every existing session out at once —
/// but it must retire the legacy credential on first use, or an old token would stay usable
/// forever alongside the new table.
/// </summary>
[Collection("Database Lane 3")]
public class RefreshSessionRotationTests : IntegrationTestBase
{
    // Isolated host: login/refresh share per-IP fixed-window buckets that Respawn cannot reset.
    private const string Password = "Str0ng!Passw0rd"; // pragma: allowlist secret (test-only)
    private const string NewPassword = "N3w!Passw0rd99"; // pragma: allowlist secret (test-only)

    public RefreshSessionRotationTests(DatabaseFixture databaseFixture) : base(databaseFixture)
    {
    }

    protected override bool RequiresIsolatedHost => true;

    [Fact]
    public async Task Refresh_RotatesTheSession_AndRetiresTheSpentToken()
    {
        var user = await SeedUserAsync("rotate@example.test");
        var first = await LoginAsync(user.Email!, Password);

        var refreshed = await RefreshAsync(first.Data!.AccessToken, first.Data.RefreshToken);
        refreshed.Success.Should().BeTrue("a live session must refresh");
        refreshed.Data!.RefreshToken.Should().NotBe(first.Data.RefreshToken, "rotation issues a NEW credential");

        // Replaying the SPENT token must fail even though it was perfectly valid one call ago.
        // This is the theft property: a captured token dies the moment the real client refreshes.
        var replay = await RefreshAsync(first.Data.AccessToken, first.Data.RefreshToken);
        replay.Success.Should().BeFalse("a rotated-out refresh token is single-use");
    }

    [Fact]
    public async Task Refresh_AcceptsTheLegacyCredential_OnceThenMigratesIt()
    {
        var user = await SeedUserAsync("legacy@example.test");
        var rawLegacy = "legacy-raw-refresh-token-value";
        var accessToken = await StampLegacyCredentialAsync(user, rawLegacy);

        var migrated = await RefreshAsync(accessToken, rawLegacy);
        migrated.Success.Should().BeTrue("the pre-migration credential must keep working across the deploy");

        // The bridge must RETIRE what it migrated: the legacy hash is erased and the live
        // credential now lives only in RefreshSessions.
        using (var scope = Factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var reloaded = await context.Users.FirstAsync(u => u.Id == user.Id);
            reloaded.RefreshToken.Should().BeEmpty("the legacy hash must not remain usable");
            reloaded.RefreshTokenExpiryTime.Should().BeOnOrBefore(DateTime.UtcNow);
            (await context.RefreshSessions.CountAsync(session => session.UserId == user.Id))
                .Should().BeGreaterThan(0, "the migrated credential is a real session now");
        }

        var replay = await RefreshAsync(accessToken, rawLegacy);
        replay.Success.Should().BeFalse("the legacy credential is single-use, like any issued session");
    }

    [Fact]
    public async Task ChangePassword_RevokesEveryLiveSession_IncludingOtherBrowsers()
    {
        await GiveTestIdentityAPasswordAsync();

        // Two real logins = two independent sessions (the multi-device property under test).
        var browserA = await LoginAsync(TestAuthHandler.UserName, Password);
        var browserB = await LoginAsync(TestAuthHandler.UserName, Password);

        // change-password resolves its caller from the test host's fixed identity.
        AuthenticateAsUser();
        var body = new StringContent(
            $"{{\"currentPassword\":\"{Password}\",\"newPassword\":\"{NewPassword}\",\"confirmPassword\":\"{NewPassword}\"}}",
            Encoding.UTF8, "application/json");
        (await Client.PostAsync("/api/Auth/change-password", body)).IsSuccessStatusCode.Should().BeTrue();

        (await RefreshAsync(browserA.Data!.AccessToken, browserA.Data.RefreshToken))
            .Success.Should().BeFalse("a password change must end every live session");
        (await RefreshAsync(browserB.Data!.AccessToken, browserB.Data.RefreshToken))
            .Success.Should().BeFalse("the OTHER browser's session must not survive either");
    }

    [Fact]
    public async Task ResetPassword_RevokesEveryLiveSession()
    {
        var user = await SeedUserAsync("reset@example.test");
        var pair = await LoginAsync(user.Email!, Password);

        // A real reset token, from the same provider the endpoint consumes.
        string token;
        using (var scope = Factory.Services.CreateScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            token = await userManager.GeneratePasswordResetTokenAsync(user);
        }

        // Identity tokens are base64-ish and can carry JSON-breaking characters — serialize,
        // never interpolate.
        var body = new StringContent(
            System.Text.Json.JsonSerializer.Serialize(new
            {
                email = user.Email,
                token,
                newPassword = NewPassword,
                confirmPassword = NewPassword,
            }),
            Encoding.UTF8,
            "application/json");
        (await Client.PostAsync("/api/Auth/reset-password", body)).IsSuccessStatusCode.Should().BeTrue();

        (await RefreshAsync(pair.Data!.AccessToken, pair.Data.RefreshToken))
            .Success.Should().BeFalse("a password RESET is the compromise response: every live session must end");
    }

    // ---- helpers ----

    private async Task<ApplicationUser> SeedUserAsync(string email)
    {
        using var scope = Factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            FirstName = "Refresh",
            LastName = "Session",
            Role = UserRole.Admin, // admin skips the customer email-confirmation branch
            CreatedBy = "test",
            RefreshToken = string.Empty,
        };

        var created = await userManager.CreateAsync(user, Password);
        created.Succeeded.Should().BeTrue(string.Join(", ", created.Errors.Select(e => e.Description)));
        return user;
    }

    private async Task<string> StampLegacyCredentialAsync(ApplicationUser user, string rawToken)
    {
        using var scope = Factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var tokens = scope.ServiceProvider.GetRequiredService<ITokenService>();

        var stored = await userManager.FindByIdAsync(user.Id.ToString());
        stored!.RefreshToken = tokens.HashRefreshToken(rawToken);
        stored.RefreshTokenExpiryTime = DateTime.UtcNow.AddDays(7);
        (await userManager.UpdateAsync(stored)).Succeeded.Should().BeTrue();

        return tokens.GenerateAccessToken(stored);
    }

    private async Task GiveTestIdentityAPasswordAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.FindByIdAsync(TestAuthHandler.UserId);
        user.Should().NotBeNull("the default seed creates the fixed test identity");
        if (!await userManager.HasPasswordAsync(user!))
        {
            (await userManager.AddPasswordAsync(user!, Password)).Succeeded.Should().BeTrue();
        }
        if (!user!.EmailConfirmed)
        {
            user.EmailConfirmed = true;
            (await userManager.UpdateAsync(user)).Succeeded.Should().BeTrue();
        }
    }

    private async Task<ApiResponse<AuthPair>> LoginAsync(string email, string password)
    {
        var json = $"{{\"email\":\"{email}\",\"password\":\"{password}\"}}";
        var response = await Client.PostAsync("/api/Auth/login", new StringContent(json, Encoding.UTF8, "application/json"));
        var body = await ReadResponseAsync<ApiResponse<AuthPair>>(response);
        body!.Success.Should().BeTrue($"login of {email} must succeed");
        return body;
    }

    private async Task<Envelope> RefreshAsync(string accessToken, string refreshToken)
    {
        var json = $"{{\"accessToken\":\"{accessToken}\",\"refreshToken\":\"{refreshToken}\"}}";
        var response = await Client.PostAsync("/api/Auth/refresh-token", new StringContent(json, Encoding.UTF8, "application/json"));
        var body = await ReadResponseAsync<ApiResponse<AuthPair>>(response);
        return new Envelope(body!.Success, body.Data);
    }

    private sealed record AuthPair(string AccessToken, string RefreshToken);

    private sealed record Envelope(bool Success, AuthPair? Data);
}
