using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Auth;

[Collection("Database Lane 2")]
public sealed class NativeSocialIdentityRaceTests(DatabaseFixture fixture) : NativeSocialFixture(fixture)
{
    [Fact]
    public async Task ConcurrentDifferentChallengesForOneSubjectResolveOneRestaurantAccount()
    {
        var first = await ChallengeAsync();
        var second = await ChallengeAsync();
        var results = await Task.WhenAll(CompleteAsync(first), CompleteAsync(second));
        Assert.All(results, result => Assert.NotNull(result));
        Assert.Equal(results[0]?.UserId, results[1]?.UserId);
        await using var context = DatabaseFixture.CreateContext();
        Assert.Single(await context.CustomerSocialIdentities.ToArrayAsync());
        Assert.Equal(2, await context.RefreshSessions.CountAsync());
        Assert.Equal(2, await context.CustomerSocialChallenges.CountAsync(row => row.ConsumedAt != null));
    }

    [Fact]
    public async Task ProviderBindingCannotMoveToAnotherLocallyReauthenticatedCustomer()
    {
        var original = await CompleteAsync(await ChallengeAsync());
        Assert.NotNull(original);
        var other = await CustomerAsync(confirmed: true);
        var challenge = await ChallengeAsync(other.Id);
        await Assert.ThrowsAsync<BadRequestException>(() => CompleteAsync(challenge, other.Id));
        await using var context = DatabaseFixture.CreateContext();
        Assert.Equal(original.UserId, (await context.CustomerSocialIdentities.SingleAsync()).UserId);
        Assert.Single(await context.RefreshSessions.ToArrayAsync());
        Assert.NotNull((await context.CustomerSocialChallenges.SingleAsync(row => row.Id == challenge.ChallengeId)).ConsumedAt);
    }

    [Fact]
    public async Task OneAccountCannotSilentlyReplaceItsExistingProviderSubject()
    {
        var customer = await CustomerAsync(confirmed: true);
        Assert.Null(await CompleteAsync(await ChallengeAsync(customer.Id), customer.Id));
        var originalSubject = Identity.Subject;
        Identity = Identity with { Subject = "another-subject" };
        var challenge = await ChallengeAsync(customer.Id);
        await Assert.ThrowsAsync<ConflictException>(() => CompleteAsync(challenge, customer.Id));
        await using var context = DatabaseFixture.CreateContext();
        Assert.Equal(originalSubject, (await context.CustomerSocialIdentities.SingleAsync()).Subject);
        Assert.Empty(await context.RefreshSessions.ToArrayAsync());
    }
}
