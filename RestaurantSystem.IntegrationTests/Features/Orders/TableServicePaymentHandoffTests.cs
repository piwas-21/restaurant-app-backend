using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Api.Features.Orders.Commands.AddTableBillPaymentCommand;
using RestaurantSystem.Api.Features.Orders.Commands.AddPaymentToOrderCommand;
using RestaurantSystem.Api.Features.TableServiceSessions.Commands.AddTableServiceSessionPaymentCommand;
using RestaurantSystem.Api.Features.TableServiceSessions.Commands.CancelTableServicePaymentHandoffCommand;
using RestaurantSystem.Api.Features.TableServiceSessions.Commands.CloseTableServiceSessionCommand;
using RestaurantSystem.Api.Features.TableServiceSessions.Commands.RequestTableServicePaymentHandoffCommand;
using RestaurantSystem.Api.Features.TableServiceSessions.Dtos;
using RestaurantSystem.Api.Features.TableServiceSessions.Queries.GetPendingTableServicePaymentHandoffsQuery;
using RestaurantSystem.Api.Features.TableServiceSessions.Services;
using RestaurantSystem.Api.Settings;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

[Collection("Database Lane 3")]
public sealed class TableServicePaymentHandoffTests : IAsyncLifetime
{
    private readonly DatabaseFixture _fixture;

    public TableServicePaymentHandoffTests(DatabaseFixture fixture) => _fixture = fixture;

    public Task InitializeAsync() => _fixture.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Server_request_captures_authoritative_bill_and_replays_without_duplicates()
    {
        var sessionId = await SeedSessionAsync(41, "CHF");
        await SeedOrderAsync(sessionId, 41, 32m);
        var operationId = Guid.NewGuid();

        var first = await RequestAsync(sessionId, 1, operationId);
        var replay = await RequestAsync(sessionId, 1, operationId);

        first.Success.Should().BeTrue();
        first.Data!.Version.Should().Be(2);
        first.Data.PaymentHandoff!.RequestedAmount.Should().Be(32m);
        first.Data.PaymentHandoff.RequestedCurrency.Should().Be("CHF");
        first.Data.PaymentHandoff.OperationId.Should().Be(operationId);
        replay.Success.Should().BeTrue();
        replay.Data!.PaymentHandoff!.HandoffId.Should().Be(first.Data.PaymentHandoff.HandoffId);
        await using var context = _fixture.CreateContext();
        (await context.TableServicePaymentHandoffs.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Reusing_handoff_operation_for_another_session_is_rejected()
    {
        var firstSession = await SeedSessionAsync(42);
        var secondSession = await SeedSessionAsync(43);
        await SeedOrderAsync(firstSession, 42, 10m);
        await SeedOrderAsync(secondSession, 43, 10m);
        var operationId = Guid.NewGuid();

        (await RequestAsync(firstSession, 1, operationId)).Success.Should().BeTrue();
        var mismatch = await RequestAsync(secondSession, 1, operationId);

        mismatch.Success.Should().BeFalse();
        mismatch.ErrorCode.Should().Be(ErrorCodes.TableServicePaymentHandoffOperationMismatch);
    }

    [Fact]
    public async Task Cashier_pending_query_exposes_the_authoritative_session_and_amount()
    {
        var sessionId = await SeedSessionAsync(47, "CHF");
        await SeedOrderAsync(sessionId, 47, 13m);
        (await RequestAsync(sessionId, 1, Guid.NewGuid())).Success.Should().BeTrue();

        await using var context = _fixture.CreateContext();
        var result = await new GetPendingTableServicePaymentHandoffsQueryHandler(
            context, Options.Create(new TableServiceSessionSettings())).Handle(
            new GetPendingTableServicePaymentHandoffsQuery(), CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Data.Should().ContainSingle().Which.Should().Match<TableServicePaymentHandoffDto>(handoff =>
            handoff.ServiceSessionId == sessionId
            && handoff.TableLabel == "47"
            && handoff.RequestedAmount == 13m
            && handoff.RequestedCurrency == "CHF");
    }

    [Fact]
    public async Task Cashier_pending_query_honours_the_configured_queue_limit()
    {
        var firstSession = await SeedSessionAsync(48);
        var secondSession = await SeedSessionAsync(49);
        await SeedOrderAsync(firstSession, 48, 5m);
        await SeedOrderAsync(secondSession, 49, 6m);
        (await RequestAsync(firstSession, 1, Guid.NewGuid())).Success.Should().BeTrue();
        (await RequestAsync(secondSession, 1, Guid.NewGuid())).Success.Should().BeTrue();

        await using var context = _fixture.CreateContext();
        var result = await new GetPendingTableServicePaymentHandoffsQueryHandler(
            context,
            Options.Create(new TableServiceSessionSettings
            {
                PendingPaymentHandoffQueueLimit = 1
            })).Handle(new GetPendingTableServicePaymentHandoffsQuery(), CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Data.Should().ContainSingle();
    }

    [Fact]
    public async Task Active_session_reader_selects_only_the_latest_handoff_in_sql()
    {
        var sessionId = await SeedSessionAsync(50);
        var olderOperation = Guid.NewGuid();
        var latestOperation = Guid.NewGuid();
        await using var context = _fixture.CreateContext();
        var session = await context.TableServiceSessions.SingleAsync(value => value.Id == sessionId);
        context.TableServicePaymentHandoffs.AddRange(
            Handoff(sessionId, olderOperation, DateTime.UtcNow.AddMinutes(-1),
                TableServicePaymentHandoffStatus.Resolved),
            Handoff(sessionId, latestOperation, DateTime.UtcNow,
                TableServicePaymentHandoffStatus.Requested));
        await context.SaveChangesAsync();

        var result = await TableServicePaymentHandoffReader.ReadLatestManyAsync(
            context, [session], CancellationToken.None);

        result.Should().ContainKey(sessionId).WhoseValue.OperationId.Should().Be(latestOperation);
    }

    [Fact]
    public async Task Partial_payment_keeps_handoff_pending_and_full_payment_resolves_exact_operation()
    {
        var sessionId = await SeedSessionAsync(44, "EUR");
        await SeedOrderAsync(sessionId, 44, 30m);
        (await RequestAsync(sessionId, 1, Guid.NewGuid())).Success.Should().BeTrue();

        var partialOperation = Guid.NewGuid();
        var partial = await PayAsync(sessionId, 2, 10m, partialOperation);
        partial.Success.Should().BeTrue();
        partial.Data!.HasPendingPaymentHandoff.Should().BeTrue();
        partial.Data.PaymentHandoff!.Status.Should().Be(nameof(TableServicePaymentHandoffStatus.Requested));

        var fullOperation = Guid.NewGuid();
        var full = await PayAsync(sessionId, 3, 20m, fullOperation);
        full.Success.Should().BeTrue();
        full.Data!.HasPendingPaymentHandoff.Should().BeFalse();
        full.Data.PaymentHandoff!.Status.Should().Be(nameof(TableServicePaymentHandoffStatus.Resolved));
        await using var context = _fixture.CreateContext();
        var paymentOperation = await context.TableBillPaymentOperations
            .SingleAsync(operation => operation.OperationId == fullOperation);
        full.Data.PaymentHandoff.ResolvedPaymentOperationId.Should().Be(paymentOperation.Id);
        paymentOperation.CreatedBy.Should().Be(nameof(UserRole.Cashier));
    }

    [Fact]
    public async Task Direct_order_payment_refuses_a_session_member_and_preserves_the_handoff()
    {
        var sessionId = await SeedSessionAsync(54);
        var orderId = await SeedOrderAsync(sessionId, 54, 10m);
        (await RequestAsync(sessionId, 1, Guid.NewGuid())).Success.Should().BeTrue();

        await using var context = _fixture.CreateContext();
        var user = User(UserRole.Cashier);
        var applicator = new OrderPaymentApplicator(
            context,
            user,
            new Mock<RestaurantSystem.Api.Features.FidelityPoints.Interfaces.IFidelityPointsService>().Object,
            new OrderPaymentReplayResolver(context),
            NullLogger<OrderPaymentApplicator>.Instance);
        var mapping = new OrderMappingService(
            context, new OrderDisplayCurrencyResolver(context), NullLogger<OrderMappingService>.Instance);
        var result = await new AddPaymentToOrderCommandHandler(context, applicator, mapping, user).Handle(
            new AddPaymentToOrderCommand
            {
                OrderId = orderId,
                OperationId = Guid.NewGuid(),
                PaymentMethod = PaymentMethod.Cash,
                Amount = 10m
            }, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.TableServiceSessionRequired);
        (await context.OrderPayments.CountAsync()).Should().Be(0);
        (await context.TableServicePaymentHandoffs.SingleAsync()).Status
            .Should().Be(TableServicePaymentHandoffStatus.Requested);
    }

    [Fact]
    public async Task Pending_handoff_blocks_close_until_versioned_cancel_is_committed()
    {
        var sessionId = await SeedSessionAsync(45);
        await SeedOrderAsync(sessionId, 45, 10m);
        (await RequestAsync(sessionId, 1, Guid.NewGuid())).Success.Should().BeTrue();

        var close = await CloseAsync(sessionId, 2);
        close.Success.Should().BeFalse();
        close.ErrorCode.Should().Be(ErrorCodes.TableServicePaymentHandoffPending);

        var cancelOperation = Guid.NewGuid();
        var cancelled = await CancelAsync(sessionId, 2, cancelOperation);
        var replay = await CancelAsync(sessionId, 2, cancelOperation);
        cancelled.Success.Should().BeTrue();
        cancelled.Data!.PaymentHandoff!.Status.Should().Be(nameof(TableServicePaymentHandoffStatus.Cancelled));
        replay.Success.Should().BeTrue();
        replay.Data!.Version.Should().Be(3);
        replay.Data.PaymentHandoff!.CancelledAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Request_and_payment_race_has_one_authoritative_versioned_winner()
    {
        var sessionId = await SeedSessionAsync(46);
        await SeedOrderAsync(sessionId, 46, 10m);

        var request = RequestAsync(sessionId, 1, Guid.NewGuid());
        var payment = PayAsync(sessionId, 1, 10m, Guid.NewGuid());
        var results = await Task.WhenAll(request, payment);

        results.Count(result => result.Success).Should().Be(1);
        await using var context = _fixture.CreateContext();
        (await context.TableServiceSessions.SingleAsync(value => value.Id == sessionId))
            .Version.Should().Be(2);
    }

    [Fact]
    public async Task Concurrent_identical_requests_replay_the_single_committed_handoff()
    {
        var sessionId = await SeedSessionAsync(48);
        await SeedOrderAsync(sessionId, 48, 10m);
        var operationId = Guid.NewGuid();

        var results = await Task.WhenAll(
            RequestAsync(sessionId, 1, operationId),
            RequestAsync(sessionId, 1, operationId));

        results.Should().OnlyContain(result => result.Success);
        results.Select(result => result.Data!.PaymentHandoff!.HandoffId).Distinct().Should().ContainSingle();
        await using var context = _fixture.CreateContext();
        (await context.TableServicePaymentHandoffs.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Concurrent_identical_cancellations_replay_the_single_committed_cancellation()
    {
        var sessionId = await SeedSessionAsync(49);
        await SeedOrderAsync(sessionId, 49, 10m);
        (await RequestAsync(sessionId, 1, Guid.NewGuid())).Success.Should().BeTrue();
        var operationId = Guid.NewGuid();

        var results = await Task.WhenAll(
            CancelAsync(sessionId, 2, operationId),
            CancelAsync(sessionId, 2, operationId));

        results.Should().OnlyContain(result => result.Success);
        results.Should().OnlyContain(result => result.Data!.Version == 3);
        await using var context = _fixture.CreateContext();
        var handoff = await context.TableServicePaymentHandoffs.SingleAsync();
        handoff.CancellationOperationId.Should().Be(operationId);
    }

    [Fact]
    public async Task Legacy_table_bill_payments_keep_and_then_resolve_the_pending_handoff()
    {
        var sessionId = await SeedSessionAsync(50);
        await SeedOrderAsync(sessionId, 50, 10m);
        (await RequestAsync(sessionId, 1, Guid.NewGuid())).Success.Should().BeTrue();

        (await PayLegacyAsync(50, 4m)).Success.Should().BeTrue();
        await using (var partialContext = _fixture.CreateContext())
        {
            (await partialContext.TableServicePaymentHandoffs.SingleAsync()).Status
                .Should().Be(TableServicePaymentHandoffStatus.Requested);
        }

        (await PayLegacyAsync(50, 6m)).Success.Should().BeTrue();
        await using var context = _fixture.CreateContext();
        var handoff = await context.TableServicePaymentHandoffs.SingleAsync();
        handoff.Status.Should().Be(TableServicePaymentHandoffStatus.Resolved);
        handoff.ResolvedPaymentOperationId.Should().NotBeNull();
        var order = await context.Orders.SingleAsync(value => value.ServiceSessionId == sessionId);
        order.Status = OrderStatus.Completed;
        await context.SaveChangesAsync();
        (await CloseAsync(sessionId, 4)).Success.Should().BeTrue();
    }

    [Fact]
    public async Task Legacy_payment_replays_the_persisted_visit_after_table_reuse()
    {
        var oldSessionId = await SeedSessionAsync(55);
        var orderId = await SeedOrderAsync(oldSessionId, 55, 10m);
        var operationId = Guid.NewGuid();

        var first = await PayLegacyAsync(55, 10m, operationId);
        first.Success.Should().BeTrue();
        await using (var context = _fixture.CreateContext())
        {
            var oldSession = await context.TableServiceSessions.SingleAsync(value => value.Id == oldSessionId);
            oldSession.Status = TableServiceSessionStatus.Closed;
            oldSession.ClosedAt = DateTime.UtcNow;
            var order = await context.Orders.SingleAsync(value => value.Id == orderId);
            order.Status = OrderStatus.Completed;
            await context.SaveChangesAsync();
        }
        await SeedSessionAsync(55);

        var replay = await PayLegacyAsync(55, 10m, operationId);

        replay.Success.Should().BeTrue();
        replay.Message.Should().Be("Payment already recorded");
        replay.Data!.ServiceSessionId.Should().Be(oldSessionId);
        await using var verify = _fixture.CreateContext();
        (await verify.TableBillPaymentOperations.CountAsync()).Should().Be(1);
        (await verify.TableBillPaymentOperations.SingleAsync()).CreatedBy
            .Should().Be(nameof(UserRole.Cashier));
        (await verify.OrderPayments.CountAsync()).Should().Be(1);
    }

    private async Task<ApiResponse<TableServiceSessionDto>> RequestAsync(
        Guid sessionId, int expectedVersion, Guid operationId)
    {
        await using var context = _fixture.CreateContext();
        var handler = new RequestTableServicePaymentHandoffCommandHandler(
            context, User(UserRole.Server), Reader(context));
        return await handler.Handle(new RequestTableServicePaymentHandoffCommand
        {
            ServiceSessionId = sessionId,
            ExpectedVersion = expectedVersion,
            OperationId = operationId
        }, CancellationToken.None);
    }

    private async Task<ApiResponse<TableServiceSessionDto>> CancelAsync(
        Guid sessionId, int expectedVersion, Guid operationId)
    {
        await using var context = _fixture.CreateContext();
        var handler = new CancelTableServicePaymentHandoffCommandHandler(
            context, User(UserRole.Server), Reader(context));
        return await handler.Handle(new CancelTableServicePaymentHandoffCommand
        {
            ServiceSessionId = sessionId,
            ExpectedVersion = expectedVersion,
            OperationId = operationId
        }, CancellationToken.None);
    }

    private async Task<ApiResponse<TableServiceSessionDto>> PayAsync(
        Guid sessionId, int expectedVersion, decimal amount, Guid operationId)
    {
        await using var context = _fixture.CreateContext();
        var applicator = new OrderPaymentApplicator(
            context,
            User(UserRole.Cashier),
            new Mock<RestaurantSystem.Api.Features.FidelityPoints.Interfaces.IFidelityPointsService>().Object,
            new OrderPaymentReplayResolver(context),
            NullLogger<OrderPaymentApplicator>.Instance);
        var reader = Reader(context);
        var handler = new AddTableServiceSessionPaymentCommandHandler(
            context,
            new TableServiceSessionPaymentWriter(context, applicator, User(UserRole.Cashier)),
            new TableServiceSessionPaymentReplayResolver(context, reader),
            reader,
            NullLogger<AddTableServiceSessionPaymentCommandHandler>.Instance,
            User(UserRole.Cashier));
        return await handler.Handle(new AddTableServiceSessionPaymentCommand
        {
            ServiceSessionId = sessionId,
            ExpectedVersion = expectedVersion,
            OperationId = operationId,
            PaymentMethod = PaymentMethod.Cash,
            Amount = amount,
        }, CancellationToken.None);
    }

    private async Task<ApiResponse<TableServiceSessionDto>> CloseAsync(Guid sessionId, int expectedVersion)
    {
        await using var context = _fixture.CreateContext();
        var reader = Reader(context);
        return await new CloseTableServiceSessionCommandHandler(context, reader).Handle(
            new CloseTableServiceSessionCommand
            {
                ServiceSessionId = sessionId,
                ExpectedVersion = expectedVersion
            }, CancellationToken.None);
    }

    private async Task<ApiResponse<RestaurantSystem.Api.Features.Orders.Dtos.TableBillDto>> PayLegacyAsync(
        int tableNumber, decimal amount, Guid? operationId = null)
    {
        await using var context = _fixture.CreateContext();
        var user = User(UserRole.Cashier);
        var applicator = new OrderPaymentApplicator(
            context,
            user,
            new Mock<RestaurantSystem.Api.Features.FidelityPoints.Interfaces.IFidelityPointsService>().Object,
            new OrderPaymentReplayResolver(context),
            NullLogger<OrderPaymentApplicator>.Instance);
        var assembler = new TableBillAssembler(
            context,
            new OrderMappingService(context, new OrderDisplayCurrencyResolver(context),
                NullLogger<OrderMappingService>.Instance),
            NullLogger<TableBillAssembler>.Instance);
        var handler = new AddTableBillPaymentCommandHandler(
            context,
            applicator,
            assembler,
            new TableBillPaymentOperationReplayResolver(context, assembler),
            NullLogger<AddTableBillPaymentCommandHandler>.Instance,
            user,
            new TableBillPaymentSessionCoordinator(context, user));
        return await handler.Handle(new AddTableBillPaymentCommand
        {
            TableNumber = tableNumber,
            OperationId = operationId ?? Guid.NewGuid(),
            Amount = amount,
            PaymentMethod = PaymentMethod.Cash,
        }, CancellationToken.None);
    }

    private TableServiceSessionReader Reader(ApplicationDbContext context) =>
        new(context, new TableBillAssembler(
            context,
            new OrderMappingService(context, new OrderDisplayCurrencyResolver(context),
                NullLogger<OrderMappingService>.Instance),
            NullLogger<TableBillAssembler>.Instance));

    private static ICurrentUserService User(UserRole role)
    {
        var user = new Mock<ICurrentUserService>();
        user.Setup(value => value.Role).Returns(role);
        user.Setup(value => value.IsAdmin).Returns(role == UserRole.Admin);
        user.Setup(value => value.GetAuditIdentifier()).Returns(role.ToString());
        user.Setup(value => value.UserId).Returns(Guid.NewGuid());
        return user.Object;
    }

    private static TableServicePaymentHandoff Handoff(
        Guid sessionId, Guid operationId, DateTime requestedAt,
        TableServicePaymentHandoffStatus status) => new()
        {
            Id = Guid.NewGuid(),
            ServiceSessionId = sessionId,
            OperationId = operationId,
            ExpectedVersion = 1,
            RequestedAmount = 5m,
            RequestedCurrency = "CHF",
            Status = status,
            RequestedAt = requestedAt,
            CreatedAt = requestedAt,
            CreatedBy = nameof(TableServicePaymentHandoffTests)
        };

    private async Task<Guid> SeedSessionAsync(int tableNumber, string? currency = null)
    {
        var id = Guid.NewGuid();
        await using var context = _fixture.CreateContext();
        context.TableServiceSessions.Add(new TableServiceSession
        {
            Id = id,
            TableNumber = tableNumber,
            Currency = currency,
            Status = TableServiceSessionStatus.Open,
            Version = 1,
            OpenedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = nameof(TableServicePaymentHandoffTests)
        });
        await context.SaveChangesAsync();
        return id;
    }

    private async Task<Guid> SeedOrderAsync(Guid sessionId, int tableNumber, decimal total)
    {
        await using var context = _fixture.CreateContext();
        var order = new Order
        {
            Id = Guid.NewGuid(),
            OrderNumber = $"HO-{Guid.NewGuid():N}"[..12],
            Type = OrderType.DineIn,
            TableNumber = tableNumber,
            ServiceSessionId = sessionId,
            Status = OrderStatus.Confirmed,
            PaymentStatus = PaymentStatus.Pending,
            SubTotal = total,
            Total = total,
            RemainingAmount = total,
            OrderDate = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = nameof(TableServicePaymentHandoffTests)
        };
        context.Orders.Add(order);
        await context.SaveChangesAsync();
        return order.Id;
    }
}
