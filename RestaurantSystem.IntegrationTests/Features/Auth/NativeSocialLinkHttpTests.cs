using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Auth.NativeSocial;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.IntegrationTests.Common;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Auth;

[Collection("Database Lane 2")]
public sealed class NativeSocialLinkHttpTests(DatabaseFixture fixture) : NativeSocialFixture(fixture)
{
    [Fact]
    public async Task CustomerExplicitLinkReauthenticatesAndReturnsNoSession()
    {
        var owner = Guid.Parse(TestAuthHandler.UserId);
        await ChangeUserAsync(owner, user => user.EmailConfirmed = true);
        using (var scope = Factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = await users.FindByIdAsync(owner.ToString());
            Assert.NotNull(user);
            var result = await users.ResetPasswordAsync(user, await users.GeneratePasswordResetTokenAsync(user), Password);
            Assert.True(result.Succeeded);
        }
        AuthenticateAsUser();
        var response = await Client.PostAsJsonAsync(Route + "google/link/challenge", new { schemaVersion = 1, currentPassword = Password });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var challenge = (await response.Content.ReadFromJsonAsync<ApiResponse<NativeSocialChallengeDto>>())?.Data;
        Assert.NotNull(challenge);
        var completed = await Client.PostAsJsonAsync(Route + "google/link/complete", new
        {
            schemaVersion = 1,
            challengeId = challenge.ChallengeId,
            challenge.Proof,
            idToken = "offline-token"
        });
        Assert.Equal(HttpStatusCode.OK, completed.StatusCode);
        Assert.True(completed.Headers.CacheControl?.NoStore);
        var body = await completed.Content.ReadAsStringAsync();
        Assert.DoesNotContain("accessToken", body, StringComparison.Ordinal);
        Assert.DoesNotContain("refreshToken", body, StringComparison.Ordinal);
        await using var context = DatabaseFixture.CreateContext();
        Assert.Equal(owner, (await context.CustomerSocialIdentities.SingleAsync()).UserId);
        Assert.Empty(await context.RefreshSessions.ToArrayAsync());
    }

    [Fact]
    public async Task AnotherAuthenticatedCustomerCannotUseTheOriginalOwnersLinkChallenge()
    {
        var original = await CustomerAsync(confirmed: true);
        var challenge = await ChallengeAsync(original.Id);
        AuthenticateAsUser();
        var response = await Client.PostAsJsonAsync(Route + "google/link/complete", new
        {
            schemaVersion = 1,
            challengeId = challenge.ChallengeId,
            challenge.Proof,
            idToken = "offline-token"
        });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await using var context = DatabaseFixture.CreateContext();
        Assert.Null((await context.CustomerSocialChallenges.SingleAsync()).ConsumedAt);
        Assert.Empty(await context.CustomerSocialIdentities.ToArrayAsync());
    }

    [Fact]
    public async Task NativeChallengeAndLegacyLoginShareOneIPBudget()
    {
        AuthenticateAsAnonymous();
        Assert.Equal(HttpStatusCode.OK, (await Client.PostAsJsonAsync(Route + "google/challenge", new { schemaVersion = 1 })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Client.PostAsJsonAsync("/api/Auth/login", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Client.PostAsJsonAsync(Route + "google/challenge", new { schemaVersion = 1 })).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await Client.PostAsJsonAsync(Route + "google/challenge", new { schemaVersion = 1 })).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await Client.PostAsJsonAsync("/api/Auth/login", new { })).StatusCode);
    }
}
