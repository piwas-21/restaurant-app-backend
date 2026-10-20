using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Moq;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Api.Features.Orders.Services.CustomerCancellation;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

[Collection("Database Lane 4")]
public sealed class CustomerOrderCancellationAtomicityTests(DatabaseFixture fixture) : CustomerOrderCancellationFixture(fixture)
{
    [Fact]
    public async Task Failed_decision_save_rolls_back_the_status_history_and_version_and_clears_tracked_changes()
    {
        var request = await AwaitingDelayAsync();
        await using var context = DatabaseFixture.CreateContext(new DecisionFailure());
        var rules = new Mock<ICustomerOrderCancellationRules>();
        rules.Setup(value => value.RefusalUnderLockAsync(It.IsAny<Order>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((CustomerOrderCancellationReason?)null);
        var owner = Owner(request.Intent.OrderId);
        var store = Store(context, rules.Object);
        var submit = () => store.SubmitAsync(owner, request.Intent, default);
        await submit.Should().ThrowAsync<IOException>();
        context.ChangeTracker.Entries().Should().BeEmpty();
        await context.SaveChangesAsync();
        var order = await context.Orders.AsNoTracking().SingleAsync();
        order.Status.Should().Be(OrderStatus.PendingApproval);
        order.Version.Should().Be(request.Intent.ExpectedVersion);
        (await context.OrderStatusHistories.CountAsync(row => row.ToStatus == OrderStatus.Cancelled)).Should().Be(0);
        (await DecisionsAsync()).Should().BeEmpty();
        (await store.SubmitAsync(owner, request.Intent, default)).Resolution.Outcome.Should().Be(CustomerOrderCancellationOutcome.Cancelled);
    }

    [Fact]
    public async Task A_preloaded_order_cannot_hide_a_newer_staff_version_from_the_locked_check()
    {
        var request = await AwaitingDelayAsync();
        await using var context = DatabaseFixture.CreateContext();
        var stale = await context.Orders.SingleAsync();
        stale.Version.Should().Be(request.Intent.ExpectedVersion);
        await ChangeAsync(stale.Id, (_, order) => { order.Status = OrderStatus.Confirmed; return Task.CompletedTask; });
        var rules = new Mock<ICustomerOrderCancellationRules>(MockBehavior.Strict);
        var result = await Store(context, rules.Object).SubmitAsync(Owner(stale.Id), request.Intent, default);
        result.Resolution.Outcome.Should().Be(CustomerOrderCancellationOutcome.Refused);
        result.Resolution.Reason.Should().Be(CustomerOrderCancellationReason.OrderChanged);
        stale.Status.Should().Be(OrderStatus.Confirmed);
        rules.VerifyNoOtherCalls();
        (await context.OrderStatusHistories.AnyAsync(row => row.ToStatus == OrderStatus.Cancelled)).Should().BeFalse();
    }

    [Fact]
    public async Task Status_and_decision_become_visible_together_only_after_the_transaction_commits()
    {
        var request = await AwaitingDelayAsync();
        var pause = new BeforeDecision();
        await using var context = DatabaseFixture.CreateContext(pause);
        var rules = new Mock<ICustomerOrderCancellationRules>();
        rules.Setup(value => value.RefusalUnderLockAsync(It.IsAny<Order>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((CustomerOrderCancellationReason?)null);
        var submit = Store(context, rules.Object).SubmitAsync(Owner(request.Intent.OrderId), request.Intent, default);
        try
        {
            await pause.Entered.Task.WaitAsync(TimeSpan.FromSeconds(20));
            await using var observer = DatabaseFixture.CreateContext();
            (await observer.Orders.AsNoTracking().SingleAsync()).Status.Should().Be(OrderStatus.PendingApproval);
            (await observer.Set<CustomerOrderCancellationOperation>().CountAsync()).Should().Be(0);
        }
        finally { pause.Release.TrySetResult(); }
        (await submit.WaitAsync(TimeSpan.FromSeconds(20))).Resolution.Outcome.Should().Be(CustomerOrderCancellationOutcome.Cancelled);
        (await DecisionsAsync()).Should().ContainSingle();
    }

    [Fact]
    public async Task Unrelated_pending_work_and_caller_transactions_are_preserved_and_refused()
    {
        var request = await AwaitingDelayAsync();
        await using var context = DatabaseFixture.CreateContext();
        var order = await context.Orders.SingleAsync();
        order.Notes = "Unrelated work";
        var store = Store(context, Mock.Of<ICustomerOrderCancellationRules>());
        var submit = () => store.SubmitAsync(Owner(order.Id), request.Intent, default);
        await submit.Should().ThrowAsync<InvalidOperationException>();
        context.ChangeTracker.HasChanges().Should().BeTrue();
        context.ChangeTracker.Clear();
        await using var transaction = await context.Database.BeginTransactionAsync();
        await submit.Should().ThrowAsync<InvalidOperationException>();
        context.Database.CurrentTransaction.Should().BeSameAs(transaction);
        await transaction.RollbackAsync();
        (await DecisionsAsync()).Should().BeEmpty();
    }

    private CustomerOrderCancellationOwner Owner(Guid orderId) =>
        new(orderId, null, CustomerOrderCancellationHash.Owner(null, Guid.Parse(Session)));

    private static CustomerOrderCancellationStore Store(ApplicationDbContext context, ICustomerOrderCancellationRules rules)
    {
        var current = new Mock<ICurrentUserService>();
        current.Setup(value => value.GetAuditIdentifier()).Returns("Atomicity fixture");
        return new(context, rules, current.Object, TimeProvider.System);
    }

    private sealed class DecisionFailure : SaveChangesInterceptor
    {
        private int _remaining = 1;
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context!.ChangeTracker.Entries<CustomerOrderCancellationOperation>().Any()
                && Interlocked.Exchange(ref _remaining, 0) == 1)
                throw new IOException("Decision persistence interrupted");
            return ValueTask.FromResult(result);
        }
    }

    private sealed class BeforeDecision : SaveChangesInterceptor
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context!.ChangeTracker.Entries<CustomerOrderCancellationOperation>().Any())
            {
                Entered.TrySetResult();
                await Release.Task.WaitAsync(TimeSpan.FromSeconds(20), cancellationToken);
            }
            return result;
        }
    }
}
