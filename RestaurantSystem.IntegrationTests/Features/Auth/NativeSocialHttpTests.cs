using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RestaurantSystem.Api.BackgroundServices;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Auth.Dtos;
using RestaurantSystem.Api.Features.Auth.NativeSocial;
using RestaurantSystem.Api.Settings;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.IntegrationTests.Common;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Auth;

[Collection("Database Lane 2")]
public sealed class NativeSocialHttpTests(DatabaseFixture fixture) : NativeSocialFixture(fixture)
{
    private const string GoogleRoute = Route + "google/";

    [Fact]
    public async Task AnonymousVersionedLoginReturnsOnlyTheSessionAfterExplicitCompletion()
    {
        AuthenticateAsAnonymous();
        var response = await Client.PostAsJsonAsync(GoogleRoute + "challenge", new { schemaVersion = 1 });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        var challenge = (await response.Content.ReadFromJsonAsync<ApiResponse<NativeSocialChallengeDto>>())?.Data;
        Assert.NotNull(challenge);
        var completed = await Client.PostAsJsonAsync(GoogleRoute + "complete", new
        {
            schemaVersion = 1,
            challengeId = challenge.ChallengeId,
            challenge.Proof,
            idToken = "offline-validated-provider-token",
            firstName = "Native",
            lastName = "Customer"
        });
        Assert.Equal(HttpStatusCode.OK, completed.StatusCode);
        Assert.True(completed.Headers.CacheControl?.NoStore);
        var body = await completed.Content.ReadAsStringAsync();
        Assert.DoesNotContain(challenge.Proof, body, StringComparison.Ordinal);
        Assert.DoesNotContain(challenge.Nonce, body, StringComparison.Ordinal);
        Assert.DoesNotContain("offline-validated-provider-token", body, StringComparison.Ordinal);
        Assert.NotNull((await completed.Content.ReadFromJsonAsync<ApiResponse<AuthResponse>>(JsonOptions))?.Data?.AccessToken);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"schemaVersion\":0}")]
    [InlineData("{\"schemaVersion\":2}")]
    [InlineData("{\"schemaVersion\":1,\"userId\":\"11111111-1111-4111-8111-111111111111\"}")]
    [InlineData("{\"schemaVersion\":1,\"provider\":\"apple\"}")]
    public async Task ChallengeRejectsMissingUnsupportedAndCallerChosenAuthority(string body)
    {
        AuthenticateAsAnonymous();
        using var content = new StringContent(body, Encoding.UTF8, "application/json");
        Assert.Equal(HttpStatusCode.BadRequest, (await Client.PostAsync(GoogleRoute + "challenge", content)).StatusCode);
        await using var context = DatabaseFixture.CreateContext();
        Assert.Empty(await context.CustomerSocialChallenges.ToArrayAsync());
    }

    [Theory]
    [InlineData(null)]
    [InlineData(UserRole.Admin)]
    [InlineData(UserRole.Server)]
    [InlineData(UserRole.Cashier)]
    public async Task LinkActionsRequireTheirAuthenticatedCustomer(UserRole? role)
    {
        if (role is { } value) AuthenticateAsRole(value); else AuthenticateAsAnonymous();
        var expected = role is null ? HttpStatusCode.Unauthorized : HttpStatusCode.Forbidden;
        Assert.Equal(expected, (await Client.PostAsJsonAsync(GoogleRoute + "link/challenge",
            new { schemaVersion = 1, currentPassword = Password })).StatusCode);
        Assert.Equal(expected, (await Client.PostAsJsonAsync(GoogleRoute + "link/complete", new
        {
            schemaVersion = 1,
            challengeId = Guid.NewGuid(),
            proof = NativeSocialChallengeProof.Create(),
            idToken = "offline-token"
        })).StatusCode);
    }

    [Theory]
    [InlineData("disabled")]
    [InlineData("read-only")]
    [InlineData("accounts")]
    [InlineData("tenant")]
    public async Task PendingLoginCannotCrossTenantOrIgnoreCapabilityWithdrawal(string withdrawal)
    {
        var challenge = await ChallengeAsync();
        using var scope = Factory.Services.CreateScope();
        var mobile = scope.ServiceProvider.GetRequiredService<IOptions<CustomerMobileSettings>>().Value;
        if (withdrawal == "disabled") scope.ServiceProvider.GetRequiredService<IOptions<CustomerNativeSocialSettings>>().Value.GoogleEnabled = false;
        if (withdrawal == "read-only") mobile.Lifecycle = "read-only";
        if (withdrawal == "accounts") mobile.EnabledCapabilities = ["menu"];
        if (withdrawal == "tenant") mobile.TenantId = Guid.NewGuid();
        AuthenticateAsAnonymous();
        var response = await Client.PostAsJsonAsync(GoogleRoute + "complete", new
        {
            schemaVersion = 1,
            challengeId = challenge.ChallengeId,
            challenge.Proof,
            idToken = "offline-token"
        });
        Assert.Equal(withdrawal == "tenant" ? HttpStatusCode.BadRequest : HttpStatusCode.NotFound, response.StatusCode);
        await using var context = DatabaseFixture.CreateContext();
        Assert.Empty(await context.CustomerSocialIdentities.ToArrayAsync());
        Assert.Empty(await context.RefreshSessions.ToArrayAsync());
    }

    [Fact]
    public async Task ExpiredPrivateRowsAreErasedAfterSignInIsDisabledWithoutDeletingLiveRows()
    {
        var expired = await ChallengeAsync();
        Clock.Advance(TimeSpan.FromMinutes(4));
        var live = await ChallengeAsync(); // issuance cleanup already removes expired; restore hostile row independently
        await using (var context = DatabaseFixture.CreateContext())
        {
            context.CustomerSocialChallenges.Add(new CustomerSocialChallenge
            {
                Id = expired.ChallengeId,
                TenantId = Tenant,
                Provider = "google",
                Flow = "login",
                Nonce = expired.Nonce,
                ProofHash = NativeSocialChallengeProof.Hash(expired.Proof),
                CreatedAt = expired.ExpiresAt.AddMinutes(-3),
                ExpiresAt = expired.ExpiresAt,
                ConsumedAt = expired.ExpiresAt.AddMinutes(-1)
            });
            await context.SaveChangesAsync();
        }
        using var scope = Factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<IOptions<CustomerNativeSocialSettings>>().Value.GoogleEnabled = false;
        var cleaner = new NativeSocialChallengeCleanupService(Factory.Services.GetRequiredService<IServiceScopeFactory>(),
            Clock, NullLogger<NativeSocialChallengeCleanupService>.Instance);
        Assert.Equal(1, await cleaner.CleanupAsync(default));
        await using var observer = DatabaseFixture.CreateContext();
        Assert.Equal(live.ChallengeId, (await observer.CustomerSocialChallenges.SingleAsync()).Id);
    }
}
