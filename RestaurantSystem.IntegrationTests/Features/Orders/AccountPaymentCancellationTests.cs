using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Npgsql;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Orders.Commands.CancelOrderCommand;
using RestaurantSystem.Api.Features.Orders.Commands.DeleteOrderCommand;
using RestaurantSystem.Api.Features.Orders.Commands.RejectDelayCommand;
using RestaurantSystem.Api.Features.Orders.Commands.UpdateOrderStatusCommand;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Api.Features.TableServiceSessions.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

[Collection("Database Lane 3")]
public sealed class AccountPaymentCancellationTests(DatabaseFixture fixture) : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task CancellationWaiterSeesAReservationCommittedWithoutAnAccountRevisionChange()
    {
        var (orderId, visitId) = await SeedAsync("cancel", AccountPaymentState.Released);
        await using var reserving = DatabaseFixture.CreateContext();
        await using var transaction = await reserving.Database.BeginTransactionAsync();
        await TableServiceSessionRowLock.LoadAsync(reserving, visitId, CancellationToken.None);
        (await reserving.AccountPaymentAttempts.SingleAsync()).State = AccountPaymentState.Reserved;
        await using var waiting = DatabaseFixture.CreateContext();
        await waiting.Database.OpenConnectionAsync();
        var pid = ((NpgsqlConnection)waiting.Database.GetDbConnection()).ProcessID;
        var cancellation = MutateAsync(waiting, orderId, "cancel");
        await using var observer = DatabaseFixture.CreateContext();
        var deadline = DateTime.UtcNow.AddSeconds(10);
        bool blocked;
        do
        {
            blocked = await observer.Database.SqlQuery<int>($"SELECT 1 AS \"Value\" FROM pg_stat_activity WHERE pid = {pid} AND wait_event_type = 'Lock'").AnyAsync();
            if (!blocked) await Task.Delay(25);
        } while (!blocked && DateTime.UtcNow < deadline);
        blocked.Should().BeTrue("the cancellation must actually wait behind the visit lock");
        await reserving.SaveChangesAsync();
        await transaction.CommitAsync();
        var completed = () => cancellation;
        await completed.Should().ThrowAsync<ConflictException>().WithMessage("*reserved or allocated*");
        await using var read = DatabaseFixture.CreateContext();
        (await read.Orders.SingleAsync()).Status.Should().Be(OrderStatus.Pending);
        (await read.TableServiceSessions.SingleAsync()).AccountRevision.Should().Be(1);
    }

    [Theory]
    [InlineData("cancel", AccountPaymentState.Reserved)]
    [InlineData("delete", AccountPaymentState.Reserved)]
    [InlineData("status", AccountPaymentState.Reserved)]
    [InlineData("delay", AccountPaymentState.Reserved)]
    [InlineData("cancel", AccountPaymentState.Captured)]
    [InlineData("delete", AccountPaymentState.Captured)]
    [InlineData("status", AccountPaymentState.Captured)]
    [InlineData("delay", AccountPaymentState.Captured)]
    public async Task EveryCancellationEntrancePreservesAllocatedUnits(string entrance, AccountPaymentState state)
    {
        var (orderId, visitId) = await SeedAsync(entrance, state);
        await using var context = DatabaseFixture.CreateContext();
        var action = () => MutateAsync(context, orderId, entrance);
        var expectedMessage = state == AccountPaymentState.Captured
            && (entrance is "cancel" or "status")
                ? "*matching tender evidence*"
                : "*reserved or allocated*";
        await action.Should().ThrowAsync<ConflictException>().WithMessage(expectedMessage);
        await using var read = DatabaseFixture.CreateContext();
        var order = await read.Orders.SingleAsync(value => value.Id == orderId);
        order.Status.Should().Be(entrance == "delay" ? OrderStatus.PendingApproval : OrderStatus.Pending);
        order.IsDeleted.Should().BeFalse();
        order.Version.Should().Be(1);
        (await read.TableServiceSessions.SingleAsync(value => value.Id == visitId)).AccountRevision.Should().Be(1);
        (await read.AccountPaymentAttempts.SingleAsync()).State.Should().Be(state);
        (await read.AccountPaymentAllocations.SingleAsync()).AmountMinor.Should().Be(100);
        (await read.OrderStatusHistories.CountAsync()).Should().Be(0);
    }

    [Theory]
    [InlineData("cancel")]
    [InlineData("delete")]
    [InlineData("status")]
    [InlineData("delay")]
    public async Task ResolvedScopeAllowsCancellationAndInvalidatesOldAccountQuotes(string entrance)
    {
        var (orderId, visitId) = await SeedAsync(entrance, AccountPaymentState.Released);
        await using var context = DatabaseFixture.CreateContext();
        await MutateAsync(context, orderId, entrance);
        await using var read = DatabaseFixture.CreateContext();
        var order = await read.Orders.IgnoreQueryFilters().SingleAsync(value => value.Id == orderId);
        order.IsDeleted.Should().Be(entrance == "delete");
        if (entrance != "delete") order.Status.Should().Be(OrderStatus.Cancelled);
        (await read.TableServiceSessions.SingleAsync(value => value.Id == visitId)).AccountRevision.Should().Be(2);
    }

    [Theory]
    [InlineData("cancel")]
    [InlineData("status")]
    public async Task LegacyVisitWithoutCapturedAllocationsCanCancelWithNullCurrencyAndRefundedManualTender(
        string entrance)
    {
        var orderId = Guid.NewGuid();
        var visitId = Guid.NewGuid();
        var paymentId = Guid.NewGuid();
        await using (var seed = DatabaseFixture.CreateContext())
        {
            var now = DateTime.UtcNow;
            seed.TableServiceSessions.Add(new TableServiceSession
            {
                Id = visitId,
                TableNumber = 985,
                Currency = null,
                AccountRevision = 1,
                OpenedAt = now,
                CreatedBy = "test"
            });
            seed.Orders.Add(new Order
            {
                Id = orderId,
                OrderNumber = $"LEGACY-{orderId:N}"[..16],
                ServiceSessionId = visitId,
                Type = OrderType.DineIn,
                Status = OrderStatus.Pending,
                PaymentStatus = PaymentStatus.Refunded,
                Total = 10m,
                TotalPaid = 10m,
                RemainingAmount = 0m,
                OrderDate = now,
                CreatedBy = "test",
                Payments =
                [
                    new OrderPayment
                    {
                        Id = paymentId,
                        PaymentMethod = PaymentMethod.Cash,
                        Amount = 10m,
                        Currency = null,
                        Status = PaymentStatus.Refunded,
                        IsRefunded = true,
                        RefundedAmount = 10m,
                        PaymentDate = now,
                        RefundDate = now,
                        RefundReason = "Earlier manual refund",
                        CreatedAt = now,
                        CreatedBy = "test"
                    }
                ]
            });
            await seed.SaveChangesAsync();
        }

        await using (var context = DatabaseFixture.CreateContext())
            await MutateAsync(context, orderId, entrance);

        await using var read = DatabaseFixture.CreateContext();
        var order = await read.Orders.Include(value => value.Payments)
            .SingleAsync(value => value.Id == orderId);
        order.Status.Should().Be(OrderStatus.Cancelled);
        order.Payments.Should().ContainSingle(value => value.Id == paymentId
            && value.Status == PaymentStatus.Refunded && value.IsRefunded
            && value.RefundedAmount == 10m && value.RefundReason == "Earlier manual refund");
        (await read.TableServiceSessions.SingleAsync(value => value.Id == visitId))
            .AccountRevision.Should().Be(2);
    }

    private static async Task MutateAsync(ApplicationDbContext context, Guid orderId, string entrance)
    {
        var current = new Mock<ICurrentUserService>();
        current.SetupGet(value => value.Role).Returns(UserRole.Admin);
        current.Setup(value => value.GetAuditIdentifier()).Returns("test");
        var mapping = new Mock<IOrderMappingService>();
        mapping.Setup(value => value.MapToOrderDtoAsync(It.IsAny<Order>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OrderDto());
        var events = new Mock<IOrderEventService>().Object;
        switch (entrance)
        {
            case "cancel":
                var cancellation = new CancelOrderCommandHandler(context, current.Object, mapping.Object,
                    new Mock<IEmailService>().Object, new Mock<IEmailLanguageResolver>().Object,
                    NullLogger<CancelOrderCommandHandler>.Instance);
                (await cancellation.Handle(new CancelOrderCommand
                { OrderId = orderId, CancellationReason = "Changed request" }, CancellationToken.None)).Success.Should().BeTrue();
                break;
            case "delete":
                var deletion = new DeleteOrderCommandHandler(context, current.Object,
                    NullLogger<DeleteOrderCommandHandler>.Instance);
                (await deletion.Handle(new DeleteOrderCommand(orderId), CancellationToken.None)).Success.Should().BeTrue();
                break;
            case "status":
                var projector = new Mock<IOrderResponseProjector>();
                projector.Setup(value => value.ProjectAsync(It.IsAny<Order>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new OrderDto());
                var status = new UpdateOrderStatusCommandHandler(context, current.Object, events, projector.Object,
                    new Mock<IOrderNotificationService>().Object, NullLogger<UpdateOrderStatusCommandHandler>.Instance);
                (await status.Handle(new UpdateOrderStatusCommand
                { OrderId = orderId, NewStatus = OrderStatus.Cancelled }, CancellationToken.None)).Success.Should().BeTrue();
                break;
            case "delay":
                var delay = new RejectDelayCommandHandler(context, events, mapping.Object,
                    NullLogger<RejectDelayCommandHandler>.Instance);
                (await delay.Handle(new RejectDelayCommand(orderId), CancellationToken.None)).Success.Should().BeTrue();
                break;
        }
    }

    private async Task<(Guid OrderId, Guid VisitId)> SeedAsync(string entrance, AccountPaymentState state)
    {
        await using var context = DatabaseFixture.CreateContext();
        var visit = new TableServiceSession
        { Id = Guid.NewGuid(), TableNumber = 986, Currency = "CHF", OpenedAt = DateTime.UtcNow, CreatedBy = "test" };
        var order = new Order
        {
            Id = Guid.NewGuid(),
            OrderNumber = "ALLOCATION-CANCEL",
            ServiceSessionId = visit.Id,
            Type = OrderType.DineIn,
            Status = entrance == "delay" ? OrderStatus.PendingApproval : OrderStatus.Pending,
            IsKitchenReleased = true,
            Total = 1m,
            RemainingAmount = 1m,
            OrderDate = DateTime.UtcNow,
            CreatedBy = "test"
        };
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
        context.TableServiceSessions.Add(visit);
        context.Orders.Add(order);
        context.AccountPaymentAttempts.Add(attempt);
        context.AccountPaymentAllocations.Add(new AccountPaymentAllocation
        {
            Id = Guid.NewGuid(),
            AttemptId = attempt.Id,
            OrderId = order.Id,
            StartOrdinal = 1,
            UnitCount = 1,
            MinorPerUnit = 100,
            AmountMinor = 100,
            CreatedBy = "test"
        });
        await context.SaveChangesAsync();
        return (order.Id, visit.Id);
    }
}
