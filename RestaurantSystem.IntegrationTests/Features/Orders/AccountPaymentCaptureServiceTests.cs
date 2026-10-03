using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.AccountPayments.Dtos;
using RestaurantSystem.Api.Features.AccountPayments.Services;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

public sealed partial class AccountPaymentCaptureWriterTests
{
    [Fact]
    public async Task ConcurrentManualCollectionCapturesOnceAndReplaySurvivesAClosedVisit()
    {
        var attemptId = await Seed(10001, 10001);
        var identity = await ReadyForCollection(attemptId);
        await using (var context = DatabaseFixture.CreateContext())
        {
            context.TableServicePaymentHandoffs.Add(new TableServicePaymentHandoff
            {
                ServiceSessionId = identity.SessionId,
                OperationId = Guid.NewGuid(),
                ExpectedVersion = 1,
                RequestedAmount = 100.01m,
                RequestedCurrency = "CHF",
                RequestedAt = DateTime.UtcNow,
                CreatedBy = "test"
            });
            await context.SaveChangesAsync();
        }
        var results = await Task.WhenAll(Collect(identity), Collect(identity));
        results.Should().OnlyContain(value => value.State == AccountPaymentState.Captured && value.Version == 2);
        await using (var context = DatabaseFixture.CreateContext())
        {
            (await context.OrderPayments.CountAsync()).Should().Be(1);
            (await context.OrderPayments.SingleAsync()).Amount.Should().Be(100.01m);
            var session = await context.TableServiceSessions.SingleAsync();
            session.AccountRevision.Should().Be(2);
            session.Status = TableServiceSessionStatus.Closed;
            var handoff = await context.TableServicePaymentHandoffs.SingleAsync();
            handoff.Status.Should().Be(TableServicePaymentHandoffStatus.Resolved);
            handoff.ResolvedAccountPaymentAttemptId.Should().Be(attemptId);
            handoff.ResolvedPaymentOperationId.Should().BeNull();
            await context.SaveChangesAsync();
        }
        (await Collect(identity)).Should().BeEquivalentTo(results[0]);
    }

    [Fact]
    public async Task PartialCollectionKeepsTheHandoffOpenForTheRemainingCent()
    {
        var attemptId = await Seed(2, 1, PaymentMethod.CreditCard);
        var identity = await ReadyForCollection(attemptId);
        await using (var context = DatabaseFixture.CreateContext())
        {
            context.TableServicePaymentHandoffs.Add(new TableServicePaymentHandoff
            {
                ServiceSessionId = identity.SessionId,
                OperationId = Guid.NewGuid(),
                ExpectedVersion = 1,
                RequestedAmount = 0.02m,
                RequestedCurrency = "CHF",
                RequestedAt = DateTime.UtcNow,
                CreatedBy = "test"
            });
            await context.SaveChangesAsync();
        }
        await Collect(identity);
        await using var read = DatabaseFixture.CreateContext();
        (await read.TableServicePaymentHandoffs.SingleAsync()).Status.Should().Be(TableServicePaymentHandoffStatus.Requested);
        (await read.Orders.SingleAsync()).RemainingAmount.Should().Be(0.01m);
    }

    [Fact]
    public async Task WrongStaffActorCannotCaptureAnExistingReservation()
    {
        var identity = await ReadyForCollection(await Seed(100, 100));
        var capture = () => Collect(identity with { ActorId = Guid.NewGuid() });
        await capture.Should().ThrowAsync<NotFoundException>();
        await using var read = DatabaseFixture.CreateContext();
        (await read.OrderPayments.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task ExpiredReservationRequiresFreshReviewBeforeManualCollection()
    {
        var identity = await ReadyForCollection(await Seed(100, 100));
        await using (var context = DatabaseFixture.CreateContext())
        {
            (await context.AccountPaymentAttempts.SingleAsync()).ReservationExpiresAt = DateTime.UtcNow.AddMinutes(-1);
            await context.SaveChangesAsync();
        }
        var capture = () => Collect(identity);
        await capture.Should().ThrowAsync<ConflictException>().WithMessage("*expired*");
        await using var read = DatabaseFixture.CreateContext();
        (await read.AccountPaymentAttempts.SingleAsync()).State.Should().Be(AccountPaymentState.Reserved);
        (await read.OrderPayments.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task LoyaltyFailureCannotRollBackCommittedMoney()
    {
        var identity = await ReadyForCollection(await Seed(100, 100));
        var fidelity = new Mock<IOrderFidelityCoordinator>();
        fidelity.Setup(value => value.AwardEarnedPointsAsync(It.IsAny<Order>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ConflictException("Injected award failure"));
        (await Collect(identity, fidelity.Object)).State.Should().Be(AccountPaymentState.Captured);
        await using var read = DatabaseFixture.CreateContext();
        (await read.AccountPaymentAttempts.SingleAsync()).State.Should().Be(AccountPaymentState.Captured);
        (await read.AccountPaymentAllocations.SingleAsync()).OrderPaymentId.Should().NotBeNull();
        (await read.OrderPayments.CountAsync()).Should().Be(1);
        (await read.TableServiceSessions.SingleAsync()).AccountRevision.Should().Be(2);
    }

    private async Task<CollectionIdentity> ReadyForCollection(Guid attemptId)
    {
        await using var context = DatabaseFixture.CreateContext();
        var attempt = await context.AccountPaymentAttempts.Include(value => value.Allocations)
            .SingleAsync(value => value.Id == attemptId);
        attempt.ReservedAt = DateTime.UtcNow;
        attempt.ReservationExpiresAt = DateTime.UtcNow.AddMinutes(5);
        var segments = attempt.Allocations.Select(value => new AccountDebtSegment(value.OrderId,
            value.OrderItemId, value.StartOrdinal, value.UnitCount, value.MinorPerUnit)).ToArray();
        var cashSettlement = AccountCashSettlementPolicy.Resolve(
            attempt.Currency, attempt.PaymentMethod, attempt.AmountMinor);
        attempt.SnapshotJson = AccountPaymentSnapshots.Serialize(new AccountPaymentQuoteSnapshot(
            1, attempt.Mode, attempt.PaymentMethod, attempt.AmountMinor, attempt.Currency, attempt.QuoteExpiresAt,
            null, null, AccountPaymentSnapshots.ToDtos(segments), cashSettlement));
        await context.SaveChangesAsync();
        return new(attempt.ServiceSessionId, attempt.OperationId, attempt.ActorId,
            attempt.PaymentMethod == PaymentMethod.Cash ? cashSettlement.DueAmountMinor : null);
    }

    [Fact]
    public async Task PostgreSqlAwardErrorRunsOutsideTheMoneyTransactionAndCannotUndoItsCapture()
    {
        var identity = await ReadyForCollection(await Seed(100, 100));
        var outsideTransaction = false;
        (await Collect(identity, failUsingSql: true, transactionProbe: value => outsideTransaction = value))
            .State.Should().Be(AccountPaymentState.Captured);
        outsideTransaction.Should().BeTrue();
        await using var read = DatabaseFixture.CreateContext();
        (await read.OrderPayments.SingleAsync()).Amount.Should().Be(1m);
        (await read.AccountPaymentAttempts.SingleAsync()).State.Should().Be(AccountPaymentState.Captured);
        (await read.AccountPaymentAllocations.SingleAsync()).OrderPaymentId.Should().NotBeNull();
    }

    private async Task<AccountPaymentOperationDto> Collect(CollectionIdentity identity,
        IOrderFidelityCoordinator? fidelity = null, bool failUsingSql = false, Action<bool>? transactionProbe = null)
    {
        await using var context = DatabaseFixture.CreateContext();
        var actors = new Mock<IAccountPaymentActorResolver>();
        actors.Setup(value => value.ResolveStaffActor()).Returns(new AccountPaymentActor(identity.ActorId,
            AccountPaymentActorKind.Staff, "test", UserRole.Cashier));
        var current = new Mock<ICurrentUserService>();
        current.Setup(value => value.GetAuditIdentifier()).Returns("test");
        if (failUsingSql)
        {
            var sqlFailure = new Mock<IOrderFidelityCoordinator>();
            sqlFailure.Setup(value => value.AwardEarnedPointsAsync(It.IsAny<Order>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
                .Returns(() =>
                {
                    transactionProbe?.Invoke(context.Database.CurrentTransaction is null);
                    return context.Database.ExecuteSqlRawAsync("SELECT 1 / 0");
                });
            fidelity = sqlFailure.Object;
        }
        var service = new AccountPaymentCaptureService(context, actors.Object,
            new AccountPaymentCaptureWriter(context, current.Object, TimeProvider.System),
            fidelity ?? new Mock<IOrderFidelityCoordinator>().Object, TimeProvider.System,
            NullLogger<AccountPaymentCaptureService>.Instance);
        return await service.CaptureManualAsync(identity.SessionId, identity.OperationId,
            new CaptureAccountPaymentRequest { ExpectedVersion = 1, ReceivedMinor = identity.ReceivedMinor },
            CancellationToken.None);
    }

    private sealed record CollectionIdentity(Guid SessionId, Guid OperationId, Guid ActorId, long? ReceivedMinor);
}
