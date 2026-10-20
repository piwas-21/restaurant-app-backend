using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.Auth.NativeSocial;
using RestaurantSystem.Api.Features.User.Services;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Auth;

[Collection("Database Lane 2")]
public sealed class NativeSocialCleanupRaceTests(DatabaseFixture fixture) : NativeSocialFixture(fixture)
{
    [Fact]
    public async Task IssuanceCleanupCannotDeadlockAnExpiringReturningLoginWaitingForTheSameUser()
    {
        var customer = await CustomerAsync(confirmed: true);
        Assert.Null(await CompleteAsync(await ChallengeAsync(customer.Id), customer.Id));
        var pendingLogin = await ChallengeAsync();
        await using var holder = DatabaseFixture.CreateContext();
        await using var transaction = await holder.Database.BeginTransactionAsync();
        await CustomerAccountWriteGuard.AcquireAsync(holder, customer.Id, true, default);
        using var createScope = Factory.Services.CreateScope();
        var createContext = createScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var createPid = await PrepareConnectionAsync(createContext);
        var creation = createScope.ServiceProvider.GetRequiredService<INativeSocialChallenges>()
            .CreateAsync("google", customer.Id, Password, default);
        using var loginScope = Factory.Services.CreateScope();
        var loginContext = loginScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var loginPid = await PrepareConnectionAsync(loginContext);
        Task? login = null;
        try
        {
            // Queue issuance first on User, then completion behind it while completion holds
            // the anonymous challenge. Releasing User must not make issuance wait on that row.
            await WaitForLockAsync(createPid);
            login = CompleteInScopeAsync(loginScope.ServiceProvider, pendingLogin, null);
            await WaitForLockAsync(loginPid);
            Clock.Advance(TimeSpan.FromMinutes(4));
            await transaction.CommitAsync();
            var fresh = await creation.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.True(fresh.ExpiresAt > Clock.GetUtcNow().UtcDateTime);
            await Assert.ThrowsAsync<BadRequestException>(() => login.WaitAsync(TimeSpan.FromSeconds(10)));
        }
        finally
        {
            await transaction.DisposeAsync();
            await creation.WaitAsync(TimeSpan.FromSeconds(10));
            if (login is not null)
            {
                try { await login.WaitAsync(TimeSpan.FromSeconds(10)); }
                catch (BadRequestException) { /* The original challenge expired during contention. */ }
            }
        }
        await using var observer = DatabaseFixture.CreateContext();
        Assert.Single(await observer.CustomerSocialIdentities.ToArrayAsync());
        Assert.Empty(await observer.RefreshSessions.ToArrayAsync());
        Assert.NotNull((await observer.CustomerSocialChallenges.SingleAsync(row => row.Id == pendingLogin.ChallengeId)).ConsumedAt);
    }
}
