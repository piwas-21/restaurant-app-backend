using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Auth.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Auth;

/// <summary>
/// fix/enforce-account-lockout: the login path now runs through
/// SignInManager.CheckPasswordSignInAsync with lockoutOnFailure, so the configured Identity
/// lockout (5 attempts / 15 min) is actually enforced. Previously CheckPasswordAsync verified the
/// password but never tracked failures, so the lockout config was dead.
///
/// Each test issues a SINGLE login request (well under the per-IP rate limit) by seeding the
/// account near the threshold, so the rate limiter never masks the lockout under test.
///
/// Google/Apple login carry no password, so they have no lockout path — intentionally not covered.
/// </summary>
public class LoginLockoutTests : IntegrationTestBase
{
    /// <summary>
    /// Its OWN host per test: the "auth" per-IP fixed window (3 / minute in test config) is host state Respawn cannot reset — a shared bucket would turn these 401 assertions into 429s.
    /// </summary>
    protected override bool RequiresIsolatedHost => true;

    private const string Password = "Str0ng!Passw0rd"; // pragma: allowlist secret (test-only)

    public LoginLockoutTests(DatabaseFixture databaseFixture) : base(databaseFixture)
    {
    }

    [Fact]
    public async Task Login_WrongPassword_RecordsAFailedAttempt()
    {
        var user = await SeedUserAsync("fail-once@example.test");

        var response = await LoginAsync(user.Email!, "WrongPassword1!");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await ReloadUserAsync(user.Id)).AccessFailedCount.Should()
            .Be(1, "a wrong password must be recorded against the lockout counter");
    }

    [Fact]
    public async Task Login_NonexistentEmail_Returns401_NotADistinctResponse()
    {
        // Enumeration boundary: an unknown email is indistinguishable from a wrong password (both
        // 401) — only a genuinely locked, existing account surfaces the distinct "locked" 200.
        var response = await LoginAsync("no-such-user@example.test", "WhateverPass1!");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Login_WrongPasswordAtThreshold_LocksTheAccount()
    {
        var user = await SeedUserAsync("locks-now@example.test", accessFailedCount: 4);

        await LoginAsync(user.Email!, "WrongPassword1!");

        (await IsLockedOutAsync(user.Id)).Should()
            .BeTrue("the 5th failed attempt must lock the account");
    }

    [Fact]
    public async Task Login_LockedAccount_IsRefusedEvenWithTheCorrectPassword()
    {
        var user = await SeedUserAsync("already-locked@example.test", lockoutEnd: DateTimeOffset.UtcNow.AddMinutes(15));

        var response = await LoginAsync(user.Email!, Password);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await ReadResponseAsync<ApiResponse<AuthResponse>>(response);
        body!.Success.Should().BeFalse();
        body.Message.Should().Contain("locked");
    }

    [Fact]
    public async Task Login_CorrectPassword_ResetsTheFailedCount()
    {
        var user = await SeedUserAsync("resets@example.test", accessFailedCount: 3);

        var response = await LoginAsync(user.Email!, Password);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await ReadResponseAsync<ApiResponse<AuthResponse>>(response);
        body!.Success.Should().BeTrue();
        (await ReloadUserAsync(user.Id)).AccessFailedCount.Should()
            .Be(0, "a successful login resets the counter");
    }

    private async Task<ApplicationUser> SeedUserAsync(
        string email, int accessFailedCount = 0, DateTimeOffset? lockoutEnd = null)
    {
        using var scope = Factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            FirstName = "Lockout",
            LastName = "Test",
            Role = UserRole.Admin, // admin skips the customer email-confirmation branch
            CreatedBy = "test",
            RefreshToken = string.Empty,
        };

        var created = await userManager.CreateAsync(user, Password);
        created.Succeeded.Should().BeTrue(string.Join(", ", created.Errors.Select(e => e.Description)));

        if (accessFailedCount > 0 || lockoutEnd is not null)
        {
            user.AccessFailedCount = accessFailedCount;
            user.LockoutEnd = lockoutEnd;
            await userManager.UpdateAsync(user);
        }

        return user;
    }

    private Task<HttpResponseMessage> LoginAsync(string email, string password)
    {
        var json = $"{{\"email\":\"{email}\",\"password\":\"{password}\"}}";
        return Client.PostAsync("/api/Auth/login", new StringContent(json, Encoding.UTF8, "application/json"));
    }

    private async Task<ApplicationUser> ReloadUserAsync(Guid id)
    {
        using var scope = Factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        return (await userManager.FindByIdAsync(id.ToString()))!;
    }

    private async Task<bool> IsLockedOutAsync(Guid id)
    {
        using var scope = Factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        return await userManager.IsLockedOutAsync((await userManager.FindByIdAsync(id.ToString()))!);
    }
}
