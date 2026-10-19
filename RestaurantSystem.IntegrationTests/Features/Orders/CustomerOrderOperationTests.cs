using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

[Collection("Database Lane 2")]
public sealed class CustomerOrderOperationTests(DatabaseFixture database) : CustomerOrderOperationFixture(database)
{
    [Fact]
    public async Task Recovering_unseen_is_read_only_and_does_not_seal_a_later_acceptance()
    {
        await using var context = Database.CreateContext();
        var store = Store(context);
        (await store.RecoverAsync(Session, Operation, Payload, default)).Should()
            .Be(new CustomerOrderOperationResolution(CustomerOrderOperationOutcome.Unseen, null));
        (await context.Set<CustomerOrderOperation>().CountAsync()).Should().Be(0);
        (await context.Orders.CountAsync()).Should().Be(0);
        var accepted = await store.ExecuteAsync(Session, Operation, Payload, () => SaveOrderAsync(context), default);
        accepted.Outcome.Should().Be(CustomerOrderOperationOutcome.Applied);
        accepted.OrderId.Should().Be((await context.Orders.SingleAsync()).Id);
    }
    [Fact]
    public async Task Concurrent_replays_return_one_order_and_execute_the_effect_once()
    {
        var contexts = Enumerable.Range(0, 8).Select(_ => Database.CreateContext()).ToArray();
        var effects = 0;
        try
        {
            var results = await Task.WhenAll(contexts.Select(context => Store(context).ExecuteAsync(Session,
                Operation, Payload, async () =>
                {
                    Interlocked.Increment(ref effects);
                    return await SaveOrderAsync(context);
                }, default)));
            effects.Should().Be(1);
            results.Select(result => result.OrderId).Distinct().Should().ContainSingle();
            results.Should().OnlyContain(result => result.Outcome == CustomerOrderOperationOutcome.Applied);
            await using var read = Database.CreateContext();
            (await read.Orders.CountAsync()).Should().Be(1);
            (await read.Set<CustomerOrderOperation>().SingleAsync()).OrderId.Should().Be(results[0].OrderId);
        }
        finally { foreach (var context in contexts) await context.DisposeAsync(); }
    }
    [Fact]
    public async Task Cancellation_seals_unseen_and_repeated_cancellation_returns_the_same_decision()
    {
        await using var context = Database.CreateContext();
        var store = Store(context);
        var cancelled = await store.CancelAsync(Session, Operation, Payload, default);
        cancelled.Should().Be(new CustomerOrderOperationResolution(CustomerOrderOperationOutcome.Cancelled, null));
        (await store.CancelAsync(Session, Operation, Payload, default)).Should().Be(cancelled);
        (await store.RecoverAsync(Session, Operation, Payload, default)).Should().Be(cancelled);
        var effects = 0;
        var late = () => store.ExecuteAsync(Session, Operation, Payload, async () =>
        {
            effects++;
            return await SaveOrderAsync(context);
        }, default);
        await late.Should().ThrowAsync<ConflictException>();
        effects.Should().Be(0);
        (await context.Orders.CountAsync()).Should().Be(0);
        (await context.Set<CustomerOrderOperation>().CountAsync()).Should().Be(1);
    }
    [Fact]
    public async Task Cancelling_an_accepted_operation_returns_its_order_without_undo_or_a_second_effect()
    {
        await using var context = Database.CreateContext();
        var store = Store(context);
        var accepted = await store.ExecuteAsync(Session, Operation, Payload, () => SaveOrderAsync(context), default);
        (await store.CancelAsync(Session, Operation, Payload, default)).Should().Be(accepted);
        (await store.RecoverAsync(Session, Operation, Payload, default)).Should().Be(accepted);
        (await context.Orders.CountAsync()).Should().Be(1);
        (await context.Set<CustomerOrderOperation>().SingleAsync()).IsCancelled.Should().BeFalse();
    }
    [Fact]
    public async Task Interrupted_effect_rolls_back_its_saved_order_and_allows_one_later_retry()
    {
        await using (var failed = Database.CreateContext())
        {
            var accept = () => Store(failed).ExecuteAsync(Session, Operation, Payload, async () =>
            {
                await SaveOrderAsync(failed);
                throw new IOException("Interrupted before operation receipt.");
            }, default);
            await accept.Should().ThrowAsync<IOException>();
        }
        await using var fresh = Database.CreateContext();
        (await fresh.Orders.CountAsync()).Should().Be(0);
        (await fresh.Set<CustomerOrderOperation>().CountAsync()).Should().Be(0);
        await Store(fresh).ExecuteAsync(Session, Operation, Payload, () => SaveOrderAsync(fresh), default);
        (await fresh.Orders.CountAsync()).Should().Be(1);
        (await fresh.Set<CustomerOrderOperation>().CountAsync()).Should().Be(1);
    }
}
