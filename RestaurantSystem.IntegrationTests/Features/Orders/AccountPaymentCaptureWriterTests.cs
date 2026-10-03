using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.AccountPayments.Services;
using RestaurantSystem.Api.Features.TableServiceSessions.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

[Collection("Database Lane 3")]
public sealed partial class AccountPaymentCaptureWriterTests(DatabaseFixture fixture) : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task OneCentContributionDoesNotEraseTheOtherUnpaidCent()
    {
        var attemptId = await Seed(2, 1);
        await Capture(attemptId);
        await using var context = DatabaseFixture.CreateContext();
        var attempt = await context.AccountPaymentAttempts.Include(value => value.Allocations)
            .SingleAsync(value => value.Id == attemptId);
        attempt.State.Should().Be(AccountPaymentState.Captured);
        attempt.Version.Should().Be(2);
        attempt.Allocations.Should().ContainSingle().Which.OrderPaymentId.Should().NotBeNull();
        var order = await context.Orders.Include(value => value.Payments).SingleAsync();
        order.TotalPaid.Should().Be(0.01m);
        order.RemainingAmount.Should().Be(0.01m);
        order.PaymentStatus.Should().Be(PaymentStatus.PartiallyPaid);
        order.Payments.Should().ContainSingle().Which.Amount.Should().Be(0.01m);
        var snapshot = await new AccountDebtSnapshotReader(context)
            .ReadAsync(attempt.ServiceSessionId, CancellationToken.None);
        snapshot.Debt.OutstandingMinor.Should().Be(1);
        snapshot.Debt.ReservedMinor.Should().Be(0);
    }

    [Fact]
    public async Task ExactFullContributionCompletesTheTenderAndLinksItsEvidence()
    {
        var attemptId = await Seed(10001, 10001);
        await Capture(attemptId);
        await using var context = DatabaseFixture.CreateContext();
        var attempt = await context.AccountPaymentAttempts.SingleAsync(value => value.Id == attemptId);
        var order = await context.Orders.Include(value => value.Payments).SingleAsync();
        order.TotalPaid.Should().Be(100.01m);
        order.RemainingAmount.Should().Be(0);
        order.PaymentStatus.Should().Be(PaymentStatus.Completed);
        var snapshot = await new AccountDebtSnapshotReader(context)
            .ReadAsync(attempt.ServiceSessionId, CancellationToken.None);
        snapshot.Debt.OutstandingMinor.Should().Be(0);
        snapshot.Debt.ReservedMinor.Should().Be(0);
    }

    [Fact]
    public async Task RollbackCannotLeaveCapturedAttemptWithoutItsMoney()
    {
        var attemptId = await Seed(10000, 600);
        await Capture(attemptId, rollback: true);
        await using var context = DatabaseFixture.CreateContext();
        var attempt = await context.AccountPaymentAttempts.Include(value => value.Allocations)
            .SingleAsync(value => value.Id == attemptId);
        attempt.State.Should().Be(AccountPaymentState.Reserved);
        attempt.Allocations.Should().OnlyContain(value => value.OrderPaymentId == null);
        (await context.OrderPayments.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task ManualPathCannotDeclareAnOnlineCapture()
    {
        var attemptId = await Seed(10000, 600, PaymentMethod.OnlinePayment);
        var capture = () => Capture(attemptId);
        await capture.Should().ThrowAsync<ConflictException>();
        await using var context = DatabaseFixture.CreateContext();
        (await context.OrderPayments.CountAsync()).Should().Be(0);
        (await context.AccountPaymentAttempts.SingleAsync(value => value.Id == attemptId))
            .State.Should().Be(AccountPaymentState.Reserved);
    }

    private async Task Capture(Guid attemptId, bool rollback = false)
    {
        await using var context = DatabaseFixture.CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();
        var sessionId = await context.AccountPaymentAttempts.Where(value => value.Id == attemptId)
            .Select(value => value.ServiceSessionId).SingleAsync();
        await TableServiceSessionRowLock.LoadAsync(context, sessionId, CancellationToken.None);
        var attempt = await context.AccountPaymentAttempts.Include(value => value.Allocations)
            .SingleAsync(value => value.Id == attemptId);
        var user = new Mock<ICurrentUserService>();
        user.Setup(value => value.GetAuditIdentifier()).Returns("test");
        await new AccountPaymentCaptureWriter(context, user.Object, TimeProvider.System)
            .RecordManualAsync(attempt, CancellationToken.None);
        await context.SaveChangesAsync();
        if (rollback) await transaction.RollbackAsync();
        else await transaction.CommitAsync();
    }

    private async Task<Guid> Seed(long chargeMinor, long contributionMinor,
        PaymentMethod method = PaymentMethod.Cash)
    {
        await using var context = DatabaseFixture.CreateContext();
        var session = new TableServiceSession
        {
            Id = Guid.NewGuid(),
            TableNumber = 988,
            Currency = "CHF",
            OpenedAt = DateTime.UtcNow,
            CreatedBy = "test"
        };
        var order = new Order
        {
            Id = Guid.NewGuid(),
            OrderNumber = "CAPTURE",
            ServiceSessionId = session.Id,
            Type = OrderType.DineIn,
            Status = OrderStatus.Completed,
            Total = chargeMinor / 100m,
            RemainingAmount = chargeMinor / 100m,
            OrderDate = DateTime.UtcNow,
            CreatedBy = "test"
        };
        var attempt = new AccountPaymentAttempt
        {
            Id = Guid.NewGuid(),
            ServiceSessionId = session.Id,
            OperationId = Guid.NewGuid(),
            ActorId = Guid.NewGuid(),
            ActorKind = AccountPaymentActorKind.Staff,
            Mode = AccountPaymentMode.Amount,
            PaymentMethod = method,
            State = AccountPaymentState.Reserved,
            AmountMinor = contributionMinor,
            Currency = "CHF",
            ExpectedAccountRevision = 1,
            PayloadHash = "test",
            SnapshotJson = "{}",
            QuoteExpiresAt = DateTime.UtcNow.AddMinutes(5),
            CreatedBy = "test"
        };
        attempt.Allocations.Add(new AccountPaymentAllocation
        {
            OrderId = order.Id,
            StartOrdinal = 1,
            UnitCount = 1,
            MinorPerUnit = contributionMinor,
            AmountMinor = contributionMinor,
            CreatedBy = "test"
        });
        context.TableServiceSessions.Add(session);
        context.Orders.Add(order);
        context.AccountPaymentAttempts.Add(attempt);
        await context.SaveChangesAsync();
        return attempt.Id;
    }
}
