using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.Auth.NativeSocial;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Auth;

[Collection("Database Lane 2")]
public sealed class NativeSocialErasureRaceTests(DatabaseFixture fixture) : NativeSocialFixture(fixture)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RealErasureCanCascadeWhileLinkCreationOrCompletionWaitsForItsOwner(bool completion)
    {
        var customer = await CustomerAsync(confirmed: true);
        var challenge = await ChallengeAsync(customer.Id);
        if (!completion) Clock.Advance(TimeSpan.FromMinutes(4)); // exercise expired-row pruning
        await using var erasure = DatabaseFixture.CreateContext();
        await PrepareConnectionAsync(erasure);
        await using var transaction = await erasure.Database.BeginTransactionAsync();
        await LockErasureAsync(erasure, customer.Id);
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var pid = await PrepareConnectionAsync(context);
        Task operation = completion ? CompleteInScopeAsync(scope.ServiceProvider, challenge, customer.Id)
            : scope.ServiceProvider.GetRequiredService<INativeSocialChallenges>()
                .CreateAsync("google", customer.Id, Password, default);
        try
        {
            await WaitForLockAsync(pid);
            await erasure.Users.IgnoreQueryFilters().Where(user => user.Id == customer.Id).ExecuteDeleteAsync();
            await transaction.CommitAsync();
            await Assert.ThrowsAsync<NotFoundException>(() => operation.WaitAsync(TimeSpan.FromSeconds(10)));
        }
        finally
        {
            await transaction.DisposeAsync();
            try { await operation.WaitAsync(TimeSpan.FromSeconds(10)); }
            catch (NotFoundException) { /* The erased owner is the expected refusal. */ }
        }
        await using var observer = DatabaseFixture.CreateContext();
        Assert.False(await observer.Users.IgnoreQueryFilters().AnyAsync(user => user.Id == customer.Id));
        Assert.Empty(await observer.CustomerSocialChallenges.ToArrayAsync());
        Assert.Empty(await observer.CustomerSocialIdentities.ToArrayAsync());
        Assert.Empty(await observer.RefreshSessions.ToArrayAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ChallengeIssuanceUsesTheClockAfterItsPoolOrExpiredRowLockWait(bool expiredRow)
    {
        Guid? expiredId = null;
        if (expiredRow)
        {
            expiredId = (await ChallengeAsync()).ChallengeId;
            Clock.Advance(TimeSpan.FromMinutes(4));
        }
        await using var holder = DatabaseFixture.CreateContext();
        await using var transaction = await holder.Database.BeginTransactionAsync();
        if (expiredId is { } id)
            await holder.CustomerSocialChallenges.FromSqlInterpolated($"SELECT * FROM customer_social_challenges WHERE id = {id} FOR UPDATE")
                .AsNoTracking().SingleAsync();
        else
            await NativeSocialLocks.PoolAsync(holder, Tenant, default);
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var pid = await PrepareConnectionAsync(context);
        var operation = scope.ServiceProvider.GetRequiredService<INativeSocialChallenges>()
            .CreateAsync("google", null, null, default);
        DateTime issuedAfter;
        try
        {
            await WaitForLockAsync(pid);
            Clock.Advance(TimeSpan.FromMinutes(4));
            issuedAfter = Clock.GetUtcNow().UtcDateTime;
        }
        finally { await transaction.CommitAsync(); }
        var challenge = await operation.WaitAsync(TimeSpan.FromSeconds(10));
        var row = await context.CustomerSocialChallenges.AsNoTracking().SingleAsync();
        Assert.True(row.CreatedAt >= issuedAfter);
        Assert.True(challenge.ExpiresAt > Clock.GetUtcNow().UtcDateTime);
        Assert.Equal(TimeSpan.FromMinutes(3), row.ExpiresAt - row.CreatedAt);
    }
}
