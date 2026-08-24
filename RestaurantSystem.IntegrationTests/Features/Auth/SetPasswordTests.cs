using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Auth.Commands.SetPasswordCommand;
using RestaurantSystem.Api.Features.Auth.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.IntegrationTests.Common;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Auth;

/// <summary>
/// Mobile BACKEND-NOTES item 3 — a Google/Apple account has no password hash, so
/// <c>POST /api/Auth/change-password</c> (which verifies a current password) can never succeed for
/// it: such a user could never move to email+password sign-in from inside the app.
/// <c>GET /api/Auth/has-password</c> tells the client which flow to show and
/// <c>POST /api/Auth/set-password</c> is the passwordless flow.
///
/// <para>
/// The caller is always <see cref="TestAuthHandler.UserId"/> — the seeded test user, which the
/// default seed creates with NO password hash, i.e. exactly the social-login shape. Tests that need
/// the other shape add a password through <c>UserManager</c> first.
/// </para>
///
/// <para>
/// The refusal on an account that already has a password is the security property here, not a
/// nicety: without it a stolen access token replaces the password of a normal account without
/// knowing it — the very thing change-password's current-password check exists to prevent.
/// </para>
/// </summary>
[Collection("Database Lane 2")]
public class SetPasswordTests : IntegrationTestBase
{
    private const string ExistingPassword = "Ex1sting!Pass"; // pragma: allowlist secret (test-only)
    private const string NewPassword = "Br4ndNew!Pass"; // pragma: allowlist secret (test-only)

    private readonly Mock<IEmailService> _email = new();

    public SetPasswordTests(DatabaseFixture databaseFixture) : base(databaseFixture)
    {
    }

    // Recording IEmailService: the "your password was changed" notification is part of the
    // contract, and a mock is the only way to assert a mail was actually handed to the service.
    protected override void ConfigureTestServices(IServiceCollection services)
    {
        services.RemoveAll<IEmailService>();
        services.AddSingleton(_email.Object);
    }

    [Fact]
    public async Task HasPassword_OnAPasswordlessAccount_ReturnsFalse()
    {
        AuthenticateAsUser();

        var body = await ReadResponseAsync<ApiResponse<bool>>(await Client.GetAsync("/api/Auth/has-password"));

        body!.Success.Should().BeTrue();
        body.Data.Should().BeFalse("a social-login account has no password hash");
    }

    [Fact]
    public async Task HasPassword_OnAnAccountWithAPassword_ReturnsTrue()
    {
        await GiveTheCallerAPasswordAsync();
        AuthenticateAsUser();

        var body = await ReadResponseAsync<ApiResponse<bool>>(await Client.GetAsync("/api/Auth/has-password"));

        body!.Success.Should().BeTrue();
        body.Data.Should().BeTrue();
    }

    [Fact]
    public async Task HasPassword_WithoutAToken_Is401()
    {
        AuthenticateAsAnonymous();

        var response = await Client.GetAsync("/api/Auth/has-password");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// The whole point of the feature: after setting one, the account can sign in with email +
    /// password, and the client's flow switch flips over.
    /// </summary>
    [Fact]
    public async Task SetPassword_OnAPasswordlessAccount_SetsIt_AndTheUserCanLogIn()
    {
        AuthenticateAsUser();

        var response = await SetPasswordAsync(NewPassword, NewPassword);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ReadResponseAsync<ApiResponse<string>>(response))!.Success.Should().BeTrue();

        var hasPassword = await ReadResponseAsync<ApiResponse<bool>>(
            await Client.GetAsync("/api/Auth/has-password"));
        hasPassword!.Data.Should().BeTrue("has-password must flip once a password exists");

        var login = await Client.PostAsync(
            "/api/Auth/login",
            new StringContent(
                $"{{\"email\":\"{TestAuthHandler.UserName}\",\"password\":\"{NewPassword}\"}}",
                Encoding.UTF8,
                "application/json"));

        login.StatusCode.Should().Be(HttpStatusCode.OK);
        var loginBody = await ReadResponseAsync<ApiResponse<AuthResponse>>(login);
        loginBody!.Success.Should().BeTrue("the password just set must be the one login accepts");
        loginBody.Data!.AccessToken.Should().NotBeNullOrWhiteSpace();
    }

    /// <summary>
    /// The security boundary. A 400 with a stable code, and — the part that actually matters — the
    /// existing password is still the one that works afterwards.
    /// </summary>
    [Fact]
    public async Task SetPassword_WhenTheAccountAlreadyHasOne_IsRefused_AndLeavesItUnchanged()
    {
        await GiveTheCallerAPasswordAsync();
        AuthenticateAsUser();

        var response = await SetPasswordAsync(NewPassword, NewPassword);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await ReadResponseAsync<ApiResponse<string>>(response);
        body!.Success.Should().BeFalse();
        body.Message.Should().Be(SetPasswordCommandHandler.AlreadyHasPasswordMessage);
        body.ErrorCode.Should().Be(ErrorCodes.PasswordAlreadySet,
            "the client switches to the change-password flow on this code rather than parsing English");

        (await CheckPasswordAsync(ExistingPassword)).Should()
            .BeTrue("a stolen token must not be able to replace a password it does not know");
        (await CheckPasswordAsync(NewPassword)).Should().BeFalse();
    }

    [Fact]
    public async Task SetPassword_WithAMismatchedConfirmation_Is400_AndSetsNothing()
    {
        AuthenticateAsUser();

        var response = await SetPasswordAsync(NewPassword, "Different1!Pass");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadResponseAsync<ApiResponse<string>>(response))!.Errors
            .Should().Contain("Passwords do not match");
        (await CallerHasPasswordAsync()).Should().BeFalse();
    }

    /// <summary>
    /// Same policy as register / reset-password / change-password — <c>MeetsPasswordPolicy</c>, not a
    /// sixth copy of the rules. "short" breaks length, uppercase, digit and special all at once.
    /// </summary>
    [Fact]
    public async Task SetPassword_WithAWeakPassword_Is400_AndSetsNothing()
    {
        AuthenticateAsUser();

        var response = await SetPasswordAsync("short", "short");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await ReadResponseAsync<ApiResponse<string>>(response);
        body!.Errors.Should().Contain("Password must be at least 8 characters long");
        body.Errors.Should().Contain("Password must contain at least one uppercase letter");
        body.Errors.Should().Contain("Password must contain at least one digit");
        body.Errors.Should().Contain("Password must contain at least one special character");
        (await CallerHasPasswordAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task SetPassword_WithoutAToken_Is401_AndSetsNothing()
    {
        AuthenticateAsAnonymous();

        var response = await SetPasswordAsync(NewPassword, NewPassword);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await CallerHasPasswordAsync()).Should().BeFalse();
    }

    /// <summary>
    /// The account is resolved from the bearer token ONLY. A body that names somebody else must be
    /// ignored in both directions: the caller gets the password, the named account keeps none.
    /// </summary>
    [Fact]
    public async Task SetPassword_IgnoresAnyUserIdentifierInTheBody()
    {
        var victim = await SeedPasswordlessUserAsync("victim@example.test");
        AuthenticateAsUser();

        var response = await Client.PostAsync(
            "/api/Auth/set-password",
            new StringContent(
                $"{{\"newPassword\":\"{NewPassword}\",\"confirmPassword\":\"{NewPassword}\"," +
                $"\"userId\":\"{victim.Id}\",\"email\":\"{victim.Email}\"}}",
                Encoding.UTF8,
                "application/json"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await CallerHasPasswordAsync()).Should().BeTrue();
        (await HasPasswordAsync(victim.Id)).Should()
            .BeFalse("the body must never be able to choose whose password is set");
    }

    /// <summary>
    /// Session side effect, identical to change-password: existing refresh tokens are dropped, so
    /// any other session must re-authenticate.
    /// </summary>
    [Fact]
    public async Task SetPassword_InvalidatesExistingRefreshTokens()
    {
        await GiveTheCallerARefreshTokenAsync();
        AuthenticateAsUser();

        (await SetPasswordAsync(NewPassword, NewPassword)).StatusCode.Should().Be(HttpStatusCode.OK);

        var user = await ReloadCallerAsync();
        user.RefreshToken.Should().BeEmpty();
        user.RefreshTokenExpiryTime.Should().BeOnOrBefore(DateTime.UtcNow);
    }

    /// <summary>
    /// M4 "password changed" (EMAIL-SPEC-TENANT-APP): the account holder is told, because setting a
    /// first password is exactly as security-relevant as changing one.
    /// </summary>
    [Fact]
    public async Task SetPassword_NotifiesTheAccountHolder()
    {
        AuthenticateAsUser();

        (await SetPasswordAsync(NewPassword, NewPassword)).StatusCode.Should().Be(HttpStatusCode.OK);

        _email.Verify(
            e => e.SendPasswordChangedNotificationAsync(
                It.IsAny<System.Globalization.CultureInfo>(),
                It.Is<ApplicationUser>(u => u.Id == Guid.Parse(TestAuthHandler.UserId))),
            Times.Once());
    }

    [Fact]
    public async Task SetPassword_WhenRefused_NotifiesNobody()
    {
        await GiveTheCallerAPasswordAsync();
        AuthenticateAsUser();

        await SetPasswordAsync(NewPassword, NewPassword);

        _email.Verify(
            e => e.SendPasswordChangedNotificationAsync(
                It.IsAny<System.Globalization.CultureInfo>(), It.IsAny<ApplicationUser>()),
            Times.Never());
    }

    private Task<HttpResponseMessage> SetPasswordAsync(string newPassword, string confirmPassword) =>
        Client.PostAsync(
            "/api/Auth/set-password",
            new StringContent(
                $"{{\"newPassword\":\"{newPassword}\",\"confirmPassword\":\"{confirmPassword}\"}}",
                Encoding.UTF8,
                "application/json"));

    private async Task GiveTheCallerAPasswordAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.FindByIdAsync(TestAuthHandler.UserId);
        var result = await userManager.AddPasswordAsync(user!, ExistingPassword);
        result.Succeeded.Should().BeTrue(string.Join(", ", result.Errors.Select(e => e.Description)));
    }

    private async Task GiveTheCallerARefreshTokenAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.FindByIdAsync(TestAuthHandler.UserId);
        user!.RefreshToken = "a-live-session-refresh-token-hash";
        user.RefreshTokenExpiryTime = DateTime.UtcNow.AddDays(7);
        (await userManager.UpdateAsync(user)).Succeeded.Should().BeTrue();
    }

    private async Task<ApplicationUser> ReloadCallerAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        return (await userManager.FindByIdAsync(TestAuthHandler.UserId))!;
    }

    private Task<bool> CallerHasPasswordAsync() => HasPasswordAsync(Guid.Parse(TestAuthHandler.UserId));

    private async Task<bool> HasPasswordAsync(Guid userId)
    {
        using var scope = Factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.FindByIdAsync(userId.ToString());
        return await userManager.HasPasswordAsync(user!);
    }

    private async Task<bool> CheckPasswordAsync(string password)
    {
        using var scope = Factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.FindByIdAsync(TestAuthHandler.UserId);
        return await userManager.CheckPasswordAsync(user!, password);
    }

    private async Task<ApplicationUser> SeedPasswordlessUserAsync(string email)
    {
        using var scope = Factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            FirstName = "Victim",
            LastName = "User",
            Role = UserRole.Customer,
            CreatedBy = "test",
            RefreshToken = string.Empty,
        };

        var created = await userManager.CreateAsync(user);
        created.Succeeded.Should().BeTrue(string.Join(", ", created.Errors.Select(e => e.Description)));
        return user;
    }
}
