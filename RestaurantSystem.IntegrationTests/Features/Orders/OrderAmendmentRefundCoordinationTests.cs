using System.Data;
using System.Data.Common;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.OrderAmendments.Dtos;
using RestaurantSystem.Api.Features.Orders.Commands.CancelOrderCommand;
using RestaurantSystem.Api.Features.Orders.Commands.RefundPaymentCommand;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Api.Features.Payments.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Common;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

[Collection("Database Lane 4")]
public sealed class OrderAmendmentRefundCoordinationTests : IAsyncLifetime
{
    private static readonly JsonSerializerOptions SnapshotOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly DatabaseFixture _fixture;

    public OrderAmendmentRefundCoordinationTests(DatabaseFixture fixture) => _fixture = fixture;

    public Task InitializeAsync() => _fixture.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Refund_waits_for_order_lock_then_refuses_a_new_pending_amendment_credit()
    {
        var (orderId, paymentId) = await SeedPaidOrderAsync();
        await using var blocker = _fixture.CreateContext();
        await using var blockerTransaction = await blocker.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted);
        blocker.Set<OrderAmendment>().Add(PendingAmendment(orderId));
        await blocker.SaveChangesAsync();
        await LockOrderAsync(blocker, orderId);

        await using var refundContext = _fixture.CreateContext();
        var backendPid = await BackendPidAsync(refundContext);
        var refund = RefundHandler(refundContext).Handle(new RefundPaymentCommand
        {
            OrderId = orderId,
            PaymentId = paymentId,
            RefundAmount = 10m,
            RefundReason = "Refund reviewed"
        }, CancellationToken.None);

        var blocked = await WaitForLockWaitAsync(backendPid, refund);
        blocked.Should().BeTrue("the refund must join the order lock before reading payment state");
        await blockerTransaction.CommitAsync();

        var error = await Record.ExceptionAsync(async () =>
            await refund.WaitAsync(TimeSpan.FromSeconds(10)));
        error.Should().BeOfType<ConflictException>()
            .Which.Message.Should().Contain("financial adjustment");
        await using var verify = _fixture.CreateContext();
        var payment = await verify.OrderPayments.SingleAsync(value => value.Id == paymentId);
        payment.RefundedAmount.Should().BeNull();
        payment.Status.Should().Be(PaymentStatus.Completed);
        (await verify.Orders.SingleAsync(value => value.Id == orderId))
            .TotalPaid.Should().Be(50m);
    }

    [Fact]
    public async Task Ordinary_local_refund_keeps_existing_partial_refund_behavior()
    {
        var (orderId, paymentId) = await SeedPaidOrderAsync();
        await using var context = _fixture.CreateContext();

        var result = await RefundHandler(context).Handle(new RefundPaymentCommand
        {
            OrderId = orderId,
            PaymentId = paymentId,
            RefundAmount = 20m,
            RefundReason = "Guest request"
        }, CancellationToken.None);

        result.Success.Should().BeTrue();
        await using var verify = _fixture.CreateContext();
        var order = await verify.Orders.SingleAsync(value => value.Id == orderId);
        var payment = await verify.OrderPayments.SingleAsync(value => value.Id == paymentId);
        payment.Status.Should().Be(PaymentStatus.PartiallyRefunded);
        payment.RefundedAmount.Should().Be(20m);
        order.TotalPaid.Should().Be(30m);
        order.RemainingAmount.Should().Be(20m);
    }

    [Fact]
    public async Task Refund_fails_closed_when_committed_amendment_resolution_has_unknown_state()
    {
        var (orderId, paymentId) = await SeedPaidOrderAsync();
        var unknown = new OrderAmendmentFinancialPreviewDto(
            "CHF", 0, 0, 0, 0,
            (OrderAmendmentFinancialResolutionStatus)99,
            OrderAmendmentCreditState.None,
            OrderAmendmentLoyaltyState.None,
            OrderAmendmentRefundState.None);
        await using (var seed = _fixture.CreateContext())
        {
            var amendment = PendingAmendment(orderId);
            amendment.FinancialResolutionJson = JsonSerializer.Serialize(unknown, SnapshotOptions);
            seed.Set<OrderAmendment>().Add(amendment);
            await seed.SaveChangesAsync();
        }

        await using var context = _fixture.CreateContext();
        var error = await Record.ExceptionAsync(() => RefundHandler(context).Handle(new RefundPaymentCommand
        {
            OrderId = orderId,
            PaymentId = paymentId,
            RefundAmount = 10m,
            RefundReason = "Refund reviewed"
        }, CancellationToken.None));

        error.Should().BeOfType<ConflictException>();
        await using var verify = _fixture.CreateContext();
        (await verify.OrderPayments.SingleAsync(value => value.Id == paymentId))
            .RefundedAmount.Should().BeNull();
    }

    [Fact]
    public async Task Cancellation_cannot_refund_a_source_with_pending_amendment_resolution()
    {
        var (orderId, paymentId) = await SeedPaidOrderAsync();
        await SeedPendingAmendmentAsync(orderId);
        await using var context = _fixture.CreateContext();
        var currentUser = StaffUser(UserRole.Admin);
        var handler = new CancelOrderCommandHandler(
            context,
            currentUser.Object,
            Mock.Of<IOrderMappingService>(),
            Mock.Of<IEmailService>(),
            TestEmailLanguages.Resolver(),
            NullLogger<CancelOrderCommandHandler>.Instance);

        var error = await Record.ExceptionAsync(() => handler.Handle(new CancelOrderCommand
        {
            OrderId = orderId,
            ExpectedVersion = 1,
            CancellationReason = "Duplicate order"
        }, CancellationToken.None));

        error.Should().BeOfType<ConflictException>();
        await using var verify = _fixture.CreateContext();
        (await verify.Orders.SingleAsync(value => value.Id == orderId))
            .Status.Should().Be(OrderStatus.Confirmed);
        (await verify.OrderPayments.SingleAsync(value => value.Id == paymentId))
            .RefundedAmount.Should().BeNull();
    }

    private async Task<(Guid OrderId, Guid PaymentId)> SeedPaidOrderAsync()
    {
        var orderId = Guid.NewGuid();
        var paymentId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        await using var context = _fixture.CreateContext();
        context.Orders.Add(new Order
        {
            Id = orderId,
            OrderNumber = $"RF-{orderId:N}"[..16],
            Type = OrderType.Takeaway,
            Status = OrderStatus.Confirmed,
            PaymentStatus = PaymentStatus.Completed,
            SubTotal = 50m,
            Total = 50m,
            TotalPaid = 50m,
            RemainingAmount = 0m,
            OrderDate = now,
            CreatedAt = now,
            CreatedBy = nameof(OrderAmendmentRefundCoordinationTests),
            Payments =
            {
                new OrderPayment
                {
                    Id = paymentId,
                    OrderId = orderId,
                    PaymentMethod = PaymentMethod.Cash,
                    Amount = 50m,
                    Status = PaymentStatus.Completed,
                    PaymentDate = now,
                    CreatedAt = now,
                    CreatedBy = nameof(OrderAmendmentRefundCoordinationTests)
                }
            }
        });
        await context.SaveChangesAsync();
        return (orderId, paymentId);
    }

    private async Task SeedPendingAmendmentAsync(Guid orderId)
    {
        await using var context = _fixture.CreateContext();
        context.Set<OrderAmendment>().Add(PendingAmendment(orderId));
        await context.SaveChangesAsync();
    }

    private static OrderAmendment PendingAmendment(Guid orderId)
    {
        var now = DateTime.UtcNow;
        var financial = new OrderAmendmentFinancialPreviewDto(
            "CHF", 0, 1000, -1000, 1000,
            OrderAmendmentFinancialResolutionStatus.Pending,
            OrderAmendmentCreditState.PendingAllocationReview,
            OrderAmendmentLoyaltyState.None,
            OrderAmendmentRefundState.PendingTillRefund);
        return new OrderAmendment
        {
            Id = Guid.NewGuid(),
            SourceOrderId = orderId,
            ActorUserId = Guid.NewGuid(),
            ActorRole = nameof(UserRole.Cashier),
            State = OrderAmendmentState.Committed,
            PayloadHash = new string('a', 64),
            CommitPayloadHash = new string('b', 64),
            ExpectedOrderVersion = 1,
            ExpiresAt = now.AddMinutes(5),
            CommittedAt = now,
            RequestJson = "{}",
            ChangesJson = "[]",
            SourceSnapshotJson = "{}",
            FinancialResolutionJson = JsonSerializer.Serialize(financial, SnapshotOptions),
            CreatedAt = now,
            CreatedBy = nameof(OrderAmendmentRefundCoordinationTests)
        };
    }

    private static RefundPaymentCommandHandler RefundHandler(ApplicationDbContext context) =>
        new(context, StaffUser(UserRole.Cashier).Object,
            NullLogger<RefundPaymentCommandHandler>.Instance);

    private static Mock<ICurrentUserService> StaffUser(UserRole role)
    {
        var user = new Mock<ICurrentUserService>();
        var id = Guid.NewGuid();
        user.Setup(value => value.Role).Returns(role);
        user.Setup(value => value.UserId).Returns(id);
        user.Setup(value => value.GetAuditIdentifier()).Returns(id.ToString());
        return user;
    }

    private static async Task<int> BackendPidAsync(ApplicationDbContext context)
    {
        var connection = context.Database.GetDbConnection();
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT pg_backend_pid()";
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private static async Task LockOrderAsync(ApplicationDbContext context, Guid orderId)
    {
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.Transaction = context.Database.CurrentTransaction!.GetDbTransaction();
        command.CommandText = "SELECT id FROM orders WHERE id = @order_id FOR UPDATE";
        AddParameter(command, "order_id", orderId);
        await command.ExecuteScalarAsync();
    }

    private async Task<bool> WaitForLockWaitAsync(int pid, Task refund)
    {
        await using var observer = _fixture.CreateContext();
        var connection = observer.Database.GetDbConnection();
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT wait_event_type FROM pg_stat_activity WHERE pid = @pid";
        AddParameter(command, "pid", pid);
        var timeout = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < timeout && !refund.IsCompleted)
        {
            var waitEvent = await command.ExecuteScalarAsync() as string;
            if (waitEvent == "Lock")
            {
                return true;
            }

            await Task.Delay(25);
        }

        return false;
    }

    private static void AddParameter(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}
