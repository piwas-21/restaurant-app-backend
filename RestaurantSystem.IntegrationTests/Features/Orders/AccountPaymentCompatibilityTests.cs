using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Common.TenantFeatures;
using RestaurantSystem.Api.Features.AccountPayments.Services;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Api.Features.Orders.Commands.RefundPaymentCommand;
using RestaurantSystem.Api.Features.Payments.Interfaces;
using RestaurantSystem.Api.Features.Payments.Services;
using RestaurantSystem.Api.Features.TableServiceSessions.Commands.AddTableServiceSessionPaymentCommand;
using RestaurantSystem.Api.Features.TableServiceSessions.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

[Collection("Database Lane 3")]
public sealed class AccountPaymentCompatibilityTests(DatabaseFixture fixture) : IntegrationTestBase(fixture)
{
    [Theory]
    [InlineData(AccountPaymentState.Reserved)]
    [InlineData(AccountPaymentState.Starting)]
    [InlineData(AccountPaymentState.Processing)]
    [InlineData(AccountPaymentState.Captured)]
    [InlineData(AccountPaymentState.CancelRequested)]
    [InlineData(AccountPaymentState.ReconciliationRequired)]
    public async Task LegacySessionTenderCannotConsumeAReviewedScope(AccountPaymentState state)
    {
        var id = await Seed(state);
        await using var context = DatabaseFixture.CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();
        var session = await TableServiceSessionRowLock.LoadAsync(context, id, CancellationToken.None);
        var applicator = new Mock<IOrderPaymentApplicator>(MockBehavior.Strict);
        var writer = new TableServiceSessionPaymentWriter(context, applicator.Object,
            new Mock<ICurrentUserService>().Object);
        var apply = () => writer.ApplyAsync(session!, new AddTableServiceSessionPaymentCommand
        {
            ServiceSessionId = id,
            ExpectedVersion = 1,
            OperationId = Guid.NewGuid(),
            Amount = 0.01m,
            Currency = "CHF",
            PaymentMethod = PaymentMethod.Cash
        }, CancellationToken.None);
        await apply.Should().ThrowAsync<ConflictException>();
        applicator.VerifyNoOtherCalls();
        context.ChangeTracker.HasChanges().Should().BeFalse();
    }

    [Theory]
    [InlineData(AccountPaymentState.Quoted)]
    [InlineData(AccountPaymentState.Released)]
    [InlineData(AccountPaymentState.Failed)]
    public async Task UnstartedOrResolvedAttemptDoesNotBlockLegacyCollection(AccountPaymentState state)
    {
        var id = await Seed(state);
        await using var context = DatabaseFixture.CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();
        await TableServiceSessionRowLock.LoadAsync(context, id, CancellationToken.None);
        await AccountPaymentLedgerGuard.RequireLegacyCollectionAsync(context, id, CancellationToken.None);
    }

    [Theory]
    [InlineData(AccountPaymentState.Reserved)]
    [InlineData(AccountPaymentState.Starting)]
    [InlineData(AccountPaymentState.Processing)]
    [InlineData(AccountPaymentState.CancelRequested)]
    [InlineData(AccountPaymentState.ReconciliationRequired)]
    public async Task PendingMoneyBlocksClosureEvenWhenRolloutFlagIsOff(AccountPaymentState state)
    {
        var id = await Seed(state);
        await using var context = DatabaseFixture.CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();
        await TableServiceSessionRowLock.LoadAsync(context, id, CancellationToken.None);
        var close = () => AccountPaymentCloseGuard.RequireClosableAsync(context, id, null, CancellationToken.None);
        await close.Should().ThrowAsync<ConflictException>();
    }

    [Fact]
    public async Task Legacy_cash_only_visit_without_currency_remains_closable_when_account_flag_is_off()
    {
        var sessionId = Guid.NewGuid();
        await using (var seed = DatabaseFixture.CreateContext())
        {
            var session = new TableServiceSession
            {
                Id = sessionId,
                TableNumber = 986,
                Currency = null,
                OpenedAt = DateTime.UtcNow,
                CreatedBy = "test"
            };
            var order = new Order
            {
                Id = Guid.NewGuid(),
                OrderNumber = "CASH-NO-CURRENCY",
                ServiceSessionId = session.Id,
                Type = OrderType.DineIn,
                Status = OrderStatus.Completed,
                Total = 1m,
                TotalPaid = 1m,
                RemainingAmount = 0m,
                PaymentStatus = PaymentStatus.Completed,
                OrderDate = DateTime.UtcNow,
                CreatedBy = "test"
            };
            order.Payments.Add(new OrderPayment
            {
                Id = Guid.NewGuid(),
                OrderId = order.Id,
                Amount = 1m,
                PaymentMethod = PaymentMethod.Cash,
                Status = PaymentStatus.Completed,
                PaymentDate = DateTime.UtcNow,
                CreatedBy = "test"
            });
            seed.TableServiceSessions.Add(session);
            seed.Orders.Add(order);
            await seed.SaveChangesAsync();
        }

        await using var context = DatabaseFixture.CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();
        var close = () => AccountPaymentCloseGuard.RequireClosableAsync(
            context, sessionId, null, CancellationToken.None);
        await close.Should().NotThrowAsync();
    }

    [Fact]
    public async Task OneUnpaidMinorUnitCannotBeWrittenOffByLegacyTolerance()
    {
        var id = await Seed(AccountPaymentState.Quoted);
        await using var context = DatabaseFixture.CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();
        await TableServiceSessionRowLock.LoadAsync(context, id, CancellationToken.None);
        var features = new Mock<ITenantFeatures>();
        features.SetupGet(value => value.TableAccountPaymentsV1).Returns(true);
        var close = () => AccountPaymentCloseGuard.RequireClosableAsync(
            context, id, features.Object, CancellationToken.None);
        await close.Should().ThrowAsync<ConflictException>().WithMessage("*exact outstanding balance*");
    }

    [Fact]
    public async Task GuardRejectsAnUnlockedWrite()
    {
        var id = await Seed(AccountPaymentState.Quoted);
        await using var context = DatabaseFixture.CreateContext();
        var apply = () => AccountPaymentLedgerGuard.RequireLegacyCollectionAsync(context, id, CancellationToken.None);
        await apply.Should().ThrowAsync<ConflictException>().WithMessage("*locked transaction*");
    }

    [Fact]
    public async Task LegacyOnlineSettlementInvalidatesTheVisitQuoteExactlyOnce()
    {
        var id = await Seed(AccountPaymentState.Quoted);
        await using var context = DatabaseFixture.CreateContext();
        var order = await context.Orders.SingleAsync(value => value.ServiceSessionId == id);
        order.Payments.Add(new OrderPayment
        {
            PaymentMethod = PaymentMethod.OnlinePayment,
            Status = PaymentStatus.Processing,
            Amount = 0.01m,
            CreatedBy = "test"
        });
        var checkout = new OrderCheckoutSession
        {
            OrderId = order.Id,
            SessionId = "cs_test_" + Guid.NewGuid().ToString("N"),
            Currency = "chf",
            AmountMinor = 1,
            IdempotencyKey = "test",
            ConnectedAccountId = "acct_test",
            CreatedBy = "test"
        };
        context.OrderCheckoutSessions.Add(checkout);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        var user = new Mock<ICurrentUserService>().Object;
        var writer = new CheckoutSettlementWriter(context, new OrderPaymentBuilder(user),
            new Mock<IOrderFidelityCoordinator>().Object, new Mock<ISettlementNotifier>().Object,
            user, NullLogger<CheckoutSettlementWriter>.Instance);
        await writer.SettleAsync(checkout, "pi_test", 1, CancellationToken.None);
        await writer.SettleAsync(checkout, "pi_test", 1, CancellationToken.None);
        await using var read = DatabaseFixture.CreateContext();
        var visit = await read.TableServiceSessions.SingleAsync(value => value.Id == id);
        visit.AccountRevision.Should().Be(2);
        visit.Version.Should().Be(2);
        var paid = await read.Orders.Include(value => value.Payments).SingleAsync(value => value.Id == order.Id);
        paid.TotalPaid.Should().Be(0.01m);
        paid.Payments.Should().ContainSingle(value => value.Status == PaymentStatus.Completed);
    }

    [Fact]
    public async Task LegacyRefundInvalidatesTheVisitQuote()
    {
        var id = await Seed(AccountPaymentState.Quoted);
        await using var context = DatabaseFixture.CreateContext();
        var order = await context.Orders.SingleAsync(value => value.ServiceSessionId == id);
        var payment = new OrderPayment
        {
            Amount = 0.01m,
            PaymentMethod = PaymentMethod.Cash,
            Status = PaymentStatus.Completed,
            Currency = "CHF",
            CreatedBy = "test"
        };
        order.Payments.Add(payment);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        var handler = new RefundPaymentCommandHandler(context, new Mock<ICurrentUserService>().Object,
            NullLogger<RefundPaymentCommandHandler>.Instance);
        var result = await handler.Handle(new RefundPaymentCommand
        {
            OrderId = order.Id,
            PaymentId = payment.Id,
            RefundAmount = 0.01m,
            RefundReason = "Return"
        }, CancellationToken.None);
        result.Success.Should().BeTrue();
        await using var read = DatabaseFixture.CreateContext();
        (await read.TableServiceSessions.SingleAsync(value => value.Id == id)).AccountRevision.Should().Be(2);
        (await read.OrderPayments.SingleAsync(value => value.Id == payment.Id)).RefundedAmount.Should().Be(0.01m);
    }

    private async Task<Guid> Seed(AccountPaymentState state)
    {
        await using var context = DatabaseFixture.CreateContext();
        var session = new TableServiceSession
        {
            Id = Guid.NewGuid(),
            TableNumber = 987,
            Currency = "CHF",
            OpenedAt = DateTime.UtcNow,
            CreatedBy = "test"
        };
        context.TableServiceSessions.Add(session);
        context.Orders.Add(new Order
        {
            Id = Guid.NewGuid(),
            OrderNumber = "COMPATIBILITY",
            ServiceSessionId = session.Id,
            Type = OrderType.DineIn,
            Status = OrderStatus.Completed,
            Total = 0.01m,
            RemainingAmount = 0.01m,
            OrderDate = DateTime.UtcNow,
            CreatedBy = "test"
        });
        context.AccountPaymentAttempts.Add(new AccountPaymentAttempt
        {
            Id = Guid.NewGuid(),
            ServiceSessionId = session.Id,
            OperationId = Guid.NewGuid(),
            ActorId = Guid.NewGuid(),
            ActorKind = AccountPaymentActorKind.Staff,
            Mode = AccountPaymentMode.Amount,
            State = state,
            AmountMinor = 1,
            Currency = "CHF",
            ExpectedAccountRevision = 1,
            PayloadHash = "test",
            SnapshotJson = "{}",
            QuoteExpiresAt = DateTime.UtcNow.AddMinutes(-1),
            CreatedBy = "test"
        });
        await context.SaveChangesAsync();
        return session.Id;
    }
}
