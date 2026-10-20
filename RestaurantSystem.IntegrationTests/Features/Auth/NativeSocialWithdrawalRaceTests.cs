using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Auth;

[Collection("Database Lane 2")]
public sealed class NativeSocialWithdrawalRaceTests(DatabaseFixture fixture) : NativeSocialFixture(fixture)
{
    private readonly AfterConsumption _pause = new();
    protected override void ConfigureTestServices(IServiceCollection services)
    {
        base.ConfigureTestServices(services);
        services.ConfigureDbContext<RestaurantSystem.Infrastructure.Persistence.ApplicationDbContext>(
            options => options.AddInterceptors(_pause));
    }

    [Theory]
    [InlineData("link", "deletion")]
    [InlineData("link", "role")]
    [InlineData("link", "stamp")]
    [InlineData("login", "deletion")]
    [InlineData("login", "role")]
    public async Task CommittedNonceCannotAuthorizeAfterAccountWithdrawalBeforeExecution(string flow, string withdrawal)
    {
        Guid owner;
        if (flow == "link") owner = (await CustomerAsync(confirmed: true)).Id;
        else
        {
            var original = await CompleteAsync(await ChallengeAsync());
            Assert.NotNull(original);
            owner = original.UserId;
        }
        var challenge = await ChallengeAsync(flow == "link" ? owner : null);
        var observedCommittedConsumption = false;
        _pause.Callback = async () =>
        {
            // This independent connection proves the nonce is already committed, not merely tracked.
            await using var observer = DatabaseFixture.CreateContext();
            var row = await observer.CustomerSocialChallenges.SingleAsync(value => value.Id == challenge.ChallengeId);
            Assert.NotNull(row.ConsumedAt);
            observedCommittedConsumption = true;
            await ChangeUserAsync(owner, user =>
            {
                if (withdrawal == "role") user.Role = UserRole.Admin;
                else if (withdrawal == "stamp") user.SecurityStamp = Guid.NewGuid().ToString();
                else user.DeletionScheduledAt = DateTime.UtcNow.AddDays(30);
            });
        };
        if (withdrawal == "role")
            await Assert.ThrowsAsync<NotFoundException>(() => CompleteAsync(challenge, flow == "link" ? owner : null));
        else
            await Assert.ThrowsAsync<BadRequestException>(() => CompleteAsync(challenge, flow == "link" ? owner : null));
        Assert.True(observedCommittedConsumption);
        await using var context = DatabaseFixture.CreateContext();
        Assert.Equal(flow == "link" ? 0 : 1, await context.CustomerSocialIdentities.CountAsync());
        Assert.Equal(flow == "link" ? 0 : 1, await context.RefreshSessions.CountAsync());
        await Assert.ThrowsAsync<BadRequestException>(() => CompleteAsync(challenge, flow == "link" ? owner : null));
    }

    private sealed class AfterConsumption : DbTransactionInterceptor
    {
        public Func<Task>? Callback { get; set; }
        public override async Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData,
            CancellationToken cancellationToken = default)
        {
            if (Callback is not { } callback) return;
            Callback = null;
            await callback();
        }
    }
}
