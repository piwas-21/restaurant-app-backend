using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.IntegrationTests.Features.Payments;

public partial class CheckoutExpirySweepTests
{
    [Theory]
    [InlineData(AccountPaymentState.Reserved)]
    [InlineData(AccountPaymentState.Processing)]
    [InlineData(AccountPaymentState.Captured)]
    [InlineData(AccountPaymentState.ReconciliationRequired)]
    public async Task ExpiryCannotCancelAnOrderWithAnAllocatedAccountContribution(AccountPaymentState state)
    {
        var seed = await SeedAsync(42.50m);
        var visitId = await AddVisitAllocationAsync(seed.OrderId, state);
        var report = await RunAsync(StripeSays("expired", "unpaid", 4250));
        report.Expired.Should().Be(1);
        report.OrdersCancelled.Should().Be(0);
        report.Failures.Should().Be(0);
        await using var read = _fixture.CreateContext();
        (await read.Orders.SingleAsync(value => value.Id == seed.OrderId)).Status.Should().Be(OrderStatus.Pending);
        (await read.TableServiceSessions.SingleAsync(value => value.Id == visitId)).AccountRevision.Should().Be(1);
        (await read.AccountPaymentAttempts.SingleAsync()).State.Should().Be(state);
        (await read.OrderStatusHistories.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task ExpiryInvalidatesVisitQuotesWhenNoScopeIsHeld()
    {
        var seed = await SeedAsync(42.50m);
        var visitId = await AddVisitAllocationAsync(seed.OrderId, AccountPaymentState.Released);
        var report = await RunAsync(StripeSays("expired", "unpaid", 4250));
        report.OrdersCancelled.Should().Be(1);
        report.Failures.Should().Be(0);
        await using var read = _fixture.CreateContext();
        (await read.Orders.SingleAsync(value => value.Id == seed.OrderId)).Status.Should().Be(OrderStatus.Cancelled);
        (await read.TableServiceSessions.SingleAsync(value => value.Id == visitId)).AccountRevision.Should().Be(2);
    }

    private async Task<Guid> AddVisitAllocationAsync(Guid orderId, AccountPaymentState state)
    {
        await using var context = _fixture.CreateContext();
        var visit = new TableServiceSession
        { Id = Guid.NewGuid(), TableNumber = 985, Currency = "CHF", OpenedAt = DateTime.UtcNow, CreatedBy = "test" };
        context.TableServiceSessions.Add(visit);
        (await context.Orders.SingleAsync(value => value.Id == orderId)).ServiceSessionId = visit.Id;
        var attempt = new AccountPaymentAttempt
        {
            Id = Guid.NewGuid(),
            ServiceSessionId = visit.Id,
            OperationId = Guid.NewGuid(),
            ActorId = Guid.NewGuid(),
            ActorKind = AccountPaymentActorKind.Staff,
            Mode = AccountPaymentMode.Amount,
            State = state,
            AmountMinor = 100,
            Currency = "CHF",
            ExpectedAccountRevision = 1,
            PayloadHash = "test",
            SnapshotJson = "{}",
            QuoteExpiresAt = DateTime.UtcNow.AddMinutes(5),
            CreatedBy = "test"
        };
        context.AccountPaymentAttempts.Add(attempt);
        context.AccountPaymentAllocations.Add(new AccountPaymentAllocation
        {
            Id = Guid.NewGuid(),
            AttemptId = attempt.Id,
            OrderId = orderId,
            StartOrdinal = 1,
            UnitCount = 1,
            MinorPerUnit = 100,
            AmountMinor = 100,
            CreatedBy = "test"
        });
        await context.SaveChangesAsync();
        return visit.Id;
    }
}
