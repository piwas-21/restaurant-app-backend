using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Auth.NativeSocial;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Auth;

[Collection("Database Lane 2")]
public sealed class NativeSocialCompletionTests(DatabaseFixture fixture) : NativeSocialFixture(fixture)
{
    [Fact]
    public async Task NewSubjectCreatesOneConfirmedCustomerBindingAndOneRefreshSession()
    {
        var response = await CompleteAsync(await ChallengeAsync());
        Assert.NotNull(response);
        Assert.Equal(UserRole.Customer, response.Role);
        Assert.False(string.IsNullOrEmpty(response.AccessToken));
        Assert.False(string.IsNullOrEmpty(response.RefreshToken));
        await using var context = DatabaseFixture.CreateContext();
        var binding = await context.CustomerSocialIdentities.SingleAsync();
        var user = await context.Users.SingleAsync(value => value.Id == response.UserId);
        Assert.Equal(Identity.Subject, binding.Subject);
        Assert.Equal(Identity.Issuer, binding.Issuer);
        Assert.Equal(Tenant, binding.TenantId);
        Assert.Equal(user.Id, binding.UserId);
        Assert.True(user.EmailConfirmed);
        Assert.Single(await context.RefreshSessions.Where(value => value.UserId == user.Id).ToArrayAsync());
    }

    [Fact]
    public async Task ExistingEmailRequiresExplicitPasswordReauthenticationAndLinkWithoutIssuingASession()
    {
        var user = await CustomerAsync(confirmed: true);
        Identity = Identity with { Email = user.Email };
        var denied = await ChallengeAsync();
        var refusal = await Assert.ThrowsAsync<BadRequestException>(() => CompleteAsync(denied));
        Assert.Equal("NATIVE_SOCIAL_LINK_REQUIRED", refusal.ErrorCode);
        await Assert.ThrowsAsync<BadRequestException>(() => CompleteAsync(denied));
        var link = await ChallengeAsync(user.Id);
        Assert.Null(await CompleteAsync(link, user.Id));
        await using var context = DatabaseFixture.CreateContext();
        Assert.Equal(user.Id, (await context.CustomerSocialIdentities.SingleAsync()).UserId);
        Assert.Empty(await context.RefreshSessions.ToArrayAsync());
        Assert.NotNull((await context.CustomerSocialChallenges.SingleAsync(value => value.Id == denied.ChallengeId)).ConsumedAt);
    }

    [Fact]
    public async Task ReturningSubjectKeepsTheOriginalAccountAndContactDespiteChangedProviderEmail()
    {
        var original = await CompleteAsync(await ChallengeAsync());
        Assert.NotNull(original);
        Identity = Identity with { Email = "changed-contact@example.test" };
        var returning = await CompleteAsync(await ChallengeAsync());
        Assert.NotNull(returning);
        Assert.Equal(original.UserId, returning.UserId);
        Assert.Equal(original.Email, returning.Email);
        Identity = Identity with { Email = null, EmailVerified = false };
        Assert.Equal(original.UserId, (await CompleteAsync(await ChallengeAsync()))?.UserId);
        await using var context = DatabaseFixture.CreateContext();
        Assert.Single(await context.CustomerSocialIdentities.ToArrayAsync());
    }

    [Theory]
    [InlineData("deletion")]
    [InlineData("role")]
    [InlineData("verification")]
    public async Task ReturningSubjectCannotIssueAfterCurrentAccountAuthorityIsWithdrawn(string withdrawal)
    {
        var original = await CompleteAsync(await ChallengeAsync());
        Assert.NotNull(original);
        await ChangeUserAsync(original.UserId, user => Withdraw(user, withdrawal));
        var challenge = await ChallengeAsync();
        if (withdrawal == "role")
            await Assert.ThrowsAsync<NotFoundException>(() => CompleteAsync(challenge));
        else
            await Assert.ThrowsAsync<BadRequestException>(() => CompleteAsync(challenge));
        await using var context = DatabaseFixture.CreateContext();
        Assert.Single(await context.RefreshSessions.ToArrayAsync());
    }

    [Fact]
    public async Task ConcurrentCompletionOfOneChallengeProducesExactlyOneSession()
    {
        var challenge = await ChallengeAsync();
        var attempts = await Task.WhenAll(Enumerable.Range(0, 2).Select(async _ =>
        {
            try { return await CompleteAsync(challenge); }
            catch (BadRequestException) { return null; }
        }));
        Assert.Single(attempts, value => value is not null);
        await using var context = DatabaseFixture.CreateContext();
        Assert.Single(await context.CustomerSocialIdentities.ToArrayAsync());
        Assert.Single(await context.RefreshSessions.ToArrayAsync());
        await Assert.ThrowsAsync<BadRequestException>(() => CompleteAsync(challenge));
    }

    [Fact]
    public async Task FailedSessionIssuanceRollsBackNewAccountAndBindingButCannotReplayTheProviderNonce()
    {
        var challenge = await ChallengeAsync();
        using var scope = Factory.Services.CreateScope();
        var realSessions = scope.ServiceProvider.GetRequiredService<IRefreshSessionService>();
        var pendingContext = scope.ServiceProvider.GetRequiredService<RestaurantSystem.Infrastructure.Persistence.ApplicationDbContext>();
        var sessions = new Mock<IRefreshSessionService>();
        var observedWrite = false;
        sessions.Setup(value => value.IssueAsync(It.IsAny<ApplicationUser>(), It.IsAny<CancellationToken>()))
            .Returns(async (ApplicationUser user, CancellationToken token) =>
            {
                await realSessions.IssueAsync(user, token);
                Assert.Single(await pendingContext.RefreshSessions.AsNoTracking().Where(value => value.UserId == user.Id).ToArrayAsync(token));
                observedWrite = true;
                throw new InvalidOperationException("Injected refusal after actual session storage.");
            });
        var completion = ActivatorUtilities.CreateInstance<NativeSocialCompletion>(scope.ServiceProvider, sessions.Object);
        await Assert.ThrowsAsync<InvalidOperationException>(() => completion.CompleteAsync(challenge.ChallengeId,
            challenge.Proof, "validated-provider-token", null, new NativeSocialProfile("Native", "Customer"), default));
        Assert.True(observedWrite);
        await using var context = DatabaseFixture.CreateContext();
        Assert.False(await context.Users.AnyAsync(value => value.Email == Identity.Email));
        Assert.Empty(await context.CustomerSocialIdentities.ToArrayAsync());
        Assert.Empty(await context.RefreshSessions.ToArrayAsync());
        Assert.NotNull((await context.CustomerSocialChallenges.SingleAsync()).ConsumedAt);
        await Assert.ThrowsAsync<BadRequestException>(() => CompleteAsync(challenge));
    }

    private static void Withdraw(ApplicationUser user, string withdrawal)
    {
        if (withdrawal == "deletion") user.DeletionScheduledAt = DateTime.UtcNow.AddDays(30);
        else if (withdrawal == "role") user.Role = UserRole.Admin;
        else if (withdrawal == "verification") user.EmailConfirmed = false;
        else user.SecurityStamp = Guid.NewGuid().ToString();
    }
}
