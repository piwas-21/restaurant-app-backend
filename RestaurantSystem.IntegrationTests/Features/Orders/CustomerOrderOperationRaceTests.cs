using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.Basket.Services.Operations;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

[Collection("Database Lane 2")]
public sealed class CustomerOrderOperationRaceTests(DatabaseFixture database) : CustomerOrderOperationFixture(database)
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);
    [Fact]
    public async Task Accepted_order_wins_a_racing_cancellation_and_blocks_other_basket_writers_until_commit()
    {
        await using var accepting = Database.CreateContext();
        await using var cancelling = Database.CreateContext();
        await using var basket = Database.CreateContext();
        await cancelling.Database.OpenConnectionAsync();
        await basket.Database.OpenConnectionAsync();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var accept = Store(accepting).ExecuteAsync(Session, Operation, Payload, async () =>
        {
            var id = await SaveOrderAsync(accepting);
            entered.TrySetResult();
            await release.Task.WaitAsync(Timeout);
            return id;
        }, default);
        await entered.Task.WaitAsync(Timeout);
        var cancel = Store(cancelling).CancelAsync(Session, Operation, Payload, default);
        var basketWrite = new BasketWriteCoordinator(basket).RunAsync(Session, null,
            () => basket.Orders.CountAsync());
        try
        {
            (await BlockedAsync(cancelling)).Should().BeTrue();
            (await BlockedAsync(basket)).Should().BeTrue();
        }
        finally { release.TrySetResult(); }
        var accepted = await accept.WaitAsync(Timeout);
        (await cancel.WaitAsync(Timeout)).Should().Be(accepted);
        (await basketWrite.WaitAsync(Timeout)).Should().Be(1);
    }
    [Fact]
    public async Task Different_guest_with_same_operation_waits_then_is_refused_without_invoking_its_effect()
    {
        await using var first = Database.CreateContext();
        await using var other = Database.CreateContext();
        await other.Database.OpenConnectionAsync();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var accept = Store(first).ExecuteAsync(Session, Operation, Payload, async () =>
        {
            var id = await SaveOrderAsync(first);
            entered.TrySetResult();
            await release.Task.WaitAsync(Timeout);
            return id;
        }, default);
        await entered.Task.WaitAsync(Timeout);
        var effects = 0;
        var foreign = Store(other).ExecuteAsync(Guid.NewGuid().ToString("D"), Operation, Payload, async () =>
        {
            Interlocked.Increment(ref effects);
            return await SaveOrderAsync(other);
        }, default);
        try { (await BlockedAsync(other)).Should().BeTrue(); }
        finally { release.TrySetResult(); }
        await accept.WaitAsync(Timeout);
        var refusal = async () => await foreign.WaitAsync(Timeout);
        await refusal.Should().ThrowAsync<NotFoundException>();
        effects.Should().Be(0);
        (await other.Orders.CountAsync()).Should().Be(1);
    }
    [Fact]
    public async Task Cancellation_committing_first_seals_a_racing_original_execution()
    {
        var pause = new CancellationPause();
        await using var cancelling = Database.CreateContext(pause);
        await using var executing = Database.CreateContext();
        await executing.Database.OpenConnectionAsync();
        var cancel = Store(cancelling).CancelAsync(Session, Operation, Payload, default);
        await pause.Entered.Task.WaitAsync(Timeout);
        var effects = 0;
        var execute = Store(executing).ExecuteAsync(Session, Operation, Payload, async () =>
        {
            Interlocked.Increment(ref effects);
            return await SaveOrderAsync(executing);
        }, default);
        try { (await BlockedAsync(executing)).Should().BeTrue(); }
        finally { pause.Release.TrySetResult(); }
        (await cancel.WaitAsync(Timeout)).Outcome.Should().Be(CustomerOrderOperationOutcome.Cancelled);
        var refusal = async () => await execute.WaitAsync(Timeout);
        await refusal.Should().ThrowAsync<ConflictException>();
        effects.Should().Be(0);
        (await executing.Orders.CountAsync()).Should().Be(0);
    }
    private Task<bool> BlockedAsync(RestaurantSystem.Infrastructure.Persistence.ApplicationDbContext context) =>
        MigrationTestSafety.WaitForBlockedMigrationAsync(Database.ConnectionString,
            ((NpgsqlConnection)context.Database.GetDbConnection()).ProcessID);
    private sealed class CancellationPause : SaveChangesInterceptor
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result,
            CancellationToken cancellationToken = default)
        {
            if (eventData.Context!.ChangeTracker.Entries<CustomerOrderOperation>().Any(entry => entry.Entity.IsCancelled))
            {
                Entered.TrySetResult();
                await Release.Task.WaitAsync(Timeout, cancellationToken);
            }
            return result;
        }
    }
}
