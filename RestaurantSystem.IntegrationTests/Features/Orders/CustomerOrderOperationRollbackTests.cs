using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

[Collection("Database Lane 2")]
public sealed class CustomerOrderOperationRollbackTests(DatabaseFixture database) : CustomerOrderOperationFixture(database)
{
    [Fact]
    public async Task Failure_before_first_save_cannot_resurrect_a_tracked_order_on_a_later_save()
    {
        await using var context = Database.CreateContext();
        var accept = () => Store(context).ExecuteAsync(Session, Operation, Payload, () =>
        {
            context.Orders.Add(new Order { Id = Guid.NewGuid(), OrderNumber = "AbortedBeforeSave", CreatedBy = "OperationFixture" });
            throw new IOException("Before first order save.");
        }, default);
        await accept.Should().ThrowAsync<IOException>();
        context.ChangeTracker.Entries().Should().BeEmpty();
        await context.SaveChangesAsync();
        (await context.Orders.CountAsync()).Should().Be(0);
        (await context.Set<CustomerOrderOperation>().CountAsync()).Should().Be(0);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Failed_receipt_save_cannot_be_persisted_later_outside_its_operation_transaction(bool cancellation)
    {
        await using var context = Database.CreateContext(new ReceiptFailure());
        var store = Store(context);
        var effect = () => cancellation
            ? store.CancelAsync(Session, Operation, Payload, default)
            : store.ExecuteAsync(Session, Operation, Payload, () => SaveOrderAsync(context), default);
        await effect.Should().ThrowAsync<IOException>();
        context.ChangeTracker.Entries().Should().BeEmpty();
        await context.SaveChangesAsync();
        (await context.Orders.CountAsync()).Should().Be(0);
        (await context.Set<CustomerOrderOperation>().CountAsync()).Should().Be(0);
        await store.ExecuteAsync(Session, Operation, Payload, () => SaveOrderAsync(context), default);
        (await context.Orders.CountAsync()).Should().Be(1);
        (await context.Set<CustomerOrderOperation>().CountAsync()).Should().Be(1);
    }
    [Fact]
    public async Task Failed_effect_does_not_leave_modified_preloaded_financial_state_pending()
    {
        await using var context = Database.CreateContext();
        var id = await SaveOrderAsync(context);
        var order = await context.Orders.SingleAsync();
        var accept = () => Store(context).ExecuteAsync(Session, Operation, Payload, () =>
        {
            order.Total = 99m;
            throw new IOException("Aborted financial change.");
        }, default);
        await accept.Should().ThrowAsync<IOException>();
        await context.SaveChangesAsync();
        (await context.Orders.SingleAsync(value => value.Id == id)).Total.Should().Be(0m);
    }
    [Fact]
    public async Task Pending_unrelated_work_is_refused_and_preserved_before_owning_any_transaction()
    {
        await using var context = Database.CreateContext();
        context.Orders.Add(new Order { Id = Guid.NewGuid(), OrderNumber = "UnrelatedPending", CreatedBy = "OperationFixture" });
        var recover = () => Store(context).RecoverAsync(Session, Operation, Payload, default);
        await recover.Should().ThrowAsync<InvalidOperationException>().WithMessage("*clean change tracker*");
        context.ChangeTracker.Entries<Order>().Single().State.Should().Be(EntityState.Added);
        context.Database.CurrentTransaction.Should().BeNull();
    }
    [Fact]
    public async Task Caller_owned_transaction_is_refused_without_committing_or_disposing_it()
    {
        await using var context = Database.CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();
        var recover = () => Store(context).RecoverAsync(Session, Operation, Payload, default);
        await recover.Should().ThrowAsync<InvalidOperationException>().WithMessage("*own their transaction*");
        context.Database.CurrentTransaction.Should().BeSameAs(transaction);
        await transaction.RollbackAsync();
    }
    [Theory]
    [InlineData("not-a-session", false, false)]
    [InlineData("00000000-0000-0000-0000-000000000000", false, false)]
    [InlineData("AAAAAAAA-AAAA-AAAA-AAAA-AAAAAAAAAAAA", false, false)]
    [InlineData(null, true, false)]
    [InlineData(null, false, true)]
    public async Task Malformed_owner_operation_and_payload_inputs_make_no_receipt(string? session, bool emptyId, bool badHash)
    {
        await using var context = Database.CreateContext();
        var recover = () => Store(context).RecoverAsync(session ?? Session, emptyId ? Guid.Empty : Operation,
            badHash ? "invalid-hash" : Payload, default);
        await recover.Should().ThrowAsync<BadRequestException>();
        (await context.Set<CustomerOrderOperation>().CountAsync()).Should().Be(0);
    }
    private sealed class ReceiptFailure : SaveChangesInterceptor
    {
        private int _remaining = 1;
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context!.ChangeTracker.Entries<CustomerOrderOperation>().Any()
                && Interlocked.Exchange(ref _remaining, 0) == 1)
                throw new IOException("Receipt persistence interrupted.");
            return ValueTask.FromResult(result);
        }
    }
}
