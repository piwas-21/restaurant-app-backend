using System.Data.Common;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Moq;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Services;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Api.Features.Orders.Services.CustomerCancellation;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

[Collection("Database Lane 4")]
public sealed class CustomerOrderCancellationErasureRaceTests(DatabaseFixture fixture) : CustomerOrderCancellationFixture(fixture)
{
    [Fact]
    public async Task A_customer_erased_while_cancellation_waits_for_the_order_cannot_leave_a_decision_or_status_change()
    {
        var request = await AwaitingDelayAsync(customer: true);
        var pause = new BeforeOrderLock();
        await using var context = DatabaseFixture.CreateContext(pause);
        var userId = (await context.Orders.AsNoTracking().SingleAsync()).UserId!.Value;
        var owner = new CustomerOrderCancellationOwner(request.Intent.OrderId, userId,
            CustomerOrderCancellationHash.Owner(userId, Guid.Empty));
        var rules = new Mock<ICustomerOrderCancellationRules>(MockBehavior.Strict);
        var store = new CustomerOrderCancellationStore(context, rules.Object, Mock.Of<ICurrentUserService>(), TimeProvider.System);
        var submit = store.SubmitAsync(owner, request.Intent, default);
        try
        {
            await pause.Entered.Task.WaitAsync(TimeSpan.FromSeconds(20));
            await using var erasure = DatabaseFixture.CreateContext();
            await using var transaction = await erasure.Database.BeginTransactionAsync();
            // The checkout has emptied its basket; account erasure removes that owned workspace.
            await erasure.Baskets.Where(value => value.UserId == userId).ExecuteDeleteAsync();
            await new RetainedCustomerDataScrubber(erasure).ScrubAsync(userId, default);
            await erasure.Users.Where(value => value.Id == userId).ExecuteDeleteAsync();
            await transaction.CommitAsync();
            pause.UserLocks.Should().Be(0, "cancellation must not hold the user before obtaining the order lock");
            pause.Release.TrySetResult();
            await FluentActions.Invoking(async () => await submit).Should().ThrowAsync<NotFoundException>();
            var retained = await context.Orders.AsNoTracking().SingleAsync();
            retained.UserId.Should().BeNull();
            retained.Status.Should().Be(OrderStatus.PendingApproval);
            (await DecisionsAsync()).Should().BeEmpty();
            rules.VerifyNoOtherCalls();
        }
        finally
        {
            pause.Release.TrySetResult();
            try { await submit.WaitAsync(TimeSpan.FromSeconds(20)); }
            catch (Exception) { /* The assertion above reports the expected refusal. */ }
        }
    }

    private sealed class BeforeOrderLock : DbCommandInterceptor
    {
        private int _entered;
        public int UserLocks { get; private set; }
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("FOR NO KEY UPDATE", StringComparison.Ordinal)
                && command.CommandText.Contains("FROM \"Users\"", StringComparison.Ordinal)) UserLocks++;
            if (command.CommandText.Contains("SELECT * FROM orders WHERE id", StringComparison.Ordinal)
                && command.CommandText.Contains("FOR UPDATE", StringComparison.Ordinal)
                && Interlocked.Exchange(ref _entered, 1) == 0)
            {
                Entered.TrySetResult();
                await Release.Task.WaitAsync(TimeSpan.FromSeconds(20), cancellationToken);
            }
            return result;
        }
    }
}
