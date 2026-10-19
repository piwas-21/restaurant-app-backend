using System.Data.Common;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Services;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

[Collection("Database Lane 4")]
public sealed class CustomerOrderCheckoutErasureRaceTests(DatabaseFixture database) : CustomerOrderCheckoutJournalFixture(database)
{
    [Fact]
    public async Task Real_erasure_can_finish_while_freeze_waits_before_its_order_lock_without_holding_the_user()
    {
        var owner = Guid.NewGuid();
        var orderId = await SeedAsync(owner);
        var barrier = new BeforeOrderLock();
        var freeze = FreezeAsync(owner, interceptors: [barrier]);
        Task? erasure = null;
        try
        {
            await barrier.Reached.Task.WaitAsync(TimeSpan.FromSeconds(10));
            erasure = EraseAsync(owner);
            await erasure.WaitAsync(TimeSpan.FromSeconds(10));
            barrier.UserLockRequests.Should().Be(0, "freezing must not hold the user while waiting for the order");
            barrier.Release.TrySetResult();
            await FluentActions.Invoking(async () => await freeze).Should().ThrowAsync<NotFoundException>();
            await using var read = Database.CreateContext();
            (await read.Orders.SingleAsync(value => value.Id == orderId)).UserId.Should().BeNull();
            (await read.Users.AnyAsync(value => value.Id == owner)).Should().BeFalse();
            await AssertNoCheckoutAsync();
        }
        finally
        {
            barrier.Release.TrySetResult();
            // Observe both operations even when the opposite lock order creates the tested cycle.
            var tasks = erasure is null ? new Task[] { freeze } : [freeze, erasure];
            await Task.WhenAll(tasks.Select(async task =>
            {
                try { await task.WaitAsync(TimeSpan.FromSeconds(15)); }
                catch (Exception) { /* The assertion above reports the failure; disposal releases locks. */ }
            }));
        }
    }

    private async Task EraseAsync(Guid owner)
    {
        await using var context = Database.CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();
        await new RetainedCustomerDataScrubber(context).ScrubAsync(owner, CancellationToken.None);
        await context.Users.Where(value => value.Id == owner).ExecuteDeleteAsync();
        await transaction.CommitAsync();
    }

    private sealed class BeforeOrderLock : DbCommandInterceptor
    {
        private int _entered;
        public int UserLockRequests { get; private set; }
        public TaskCompletionSource Reached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("FOR NO KEY UPDATE", StringComparison.Ordinal)
                && command.CommandText.Contains("FROM \"Users\"", StringComparison.Ordinal)) UserLockRequests++;
            if (command.CommandText.Contains("SELECT * FROM orders WHERE id", StringComparison.Ordinal)
                && command.CommandText.Contains("FOR UPDATE", StringComparison.Ordinal)
                && Interlocked.Exchange(ref _entered, 1) == 0)
            {
                Reached.TrySetResult();
                await Release.Task.WaitAsync(cancellationToken);
            }
            return result;
        }
    }
}
