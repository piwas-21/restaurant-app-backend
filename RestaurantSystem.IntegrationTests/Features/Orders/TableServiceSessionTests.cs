using System.Data.Common;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Orders.Queries.GetTableBillQuery;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Api.Features.TableServiceSessions.Commands.AddTableServiceSessionPaymentCommand;
using RestaurantSystem.Api.Features.TableServiceSessions.Commands.CloseTableServiceSessionCommand;
using RestaurantSystem.Api.Features.TableServiceSessions.Commands.OpenTableServiceSessionCommand;
using RestaurantSystem.Api.Features.TableServiceSessions.Dtos;
using RestaurantSystem.Api.Features.TableServiceSessions.Services;
using RestaurantSystem.Api.Features.TableServiceSessions.Queries.GetTableServiceSessionPaymentOperationQuery;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

/// <summary>
/// PostgreSQL contract tests for explicit table visits. These tests deliberately seed two rounds
/// and legacy rows with the same table number: table number is not a membership key anymore.
/// </summary>
[Collection("Database Lane 3")]
public sealed class TableServiceSessionTests : IAsyncLifetime
{
    private readonly DatabaseFixture _fixture;

    public TableServiceSessionTests(DatabaseFixture fixture) => _fixture = fixture;

    public Task InitializeAsync() => _fixture.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Open_by_stable_table_id_preserves_alphanumeric_label_and_replays_authoritative_session()
    {
        var tableId = await SeedTableAsync("T-QA");
        await using var context = _fixture.CreateContext();
        var handler = OpenHandler(context);

        var first = await handler.Handle(new OpenTableServiceSessionCommand
        {
            TableId = tableId,
            Currency = "CHF"
        }, CancellationToken.None);
        var retry = await handler.Handle(new OpenTableServiceSessionCommand
        {
            TableId = tableId,
            Currency = "CHF"
        }, CancellationToken.None);

        first.Success.Should().BeTrue();
        first.Data!.TableId.Should().Be(tableId);
        first.Data.TableNumber.Should().BeNull();
        first.Data.TableLabel.Should().Be("T-QA");
        retry.Success.Should().BeTrue();
        retry.Data!.ServiceSessionId.Should().Be(first.Data.ServiceSessionId);
        (await context.TableServiceSessions.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Leading_zero_label_does_not_claim_a_numeric_compatibility_number()
    {
        var tableId = await SeedTableAsync("007");

        await using var context = _fixture.CreateContext();
        var result = await OpenHandler(context).Handle(
            new OpenTableServiceSessionCommand { TableId = tableId }, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Data!.TableId.Should().Be(tableId);
        result.Data.TableNumber.Should().BeNull();
        result.Data.TableLabel.Should().Be("007");
    }

    [Fact]
    public async Task Stable_session_read_and_close_only_consider_unassigned_orders_for_that_table()
    {
        var tableId = await SeedTableAsync("T-QA");
        var otherTableId = await SeedTableAsync("T-OTHER");
        var sessionId = await SeedStableSessionAsync(tableId);
        await SeedStableOrderAsync(null, tableId, "T-QA", 10m, Utc(12, 0));
        await SeedStableOrderAsync(null, otherTableId, "T-OTHER", 20m, Utc(12, 1));

        await using var context = _fixture.CreateContext();
        var reader = new TableServiceSessionReader(context, Assembler());
        var dto = await reader.ReadAsync(sessionId, CancellationToken.None);

        dto.Should().NotBeNull();
        dto!.HasUnassignedActiveOrders.Should().BeTrue();
        dto.LegacyActiveOrderCount.Should().Be(1);
        dto.CanClose.Should().BeFalse();

        var close = await CloseAsync(sessionId, expectedVersion: 1);

        close.Success.Should().BeFalse();
        close.ErrorCode.Should().Be(ErrorCodes.TableServiceSessionAmbiguous);
        close.Errors.Should().ContainSingle(TableBillTargetResolver.AmbiguousMessage);
    }

    [Fact]
    public async Task Stable_session_ignores_unassigned_orders_for_another_alphanumeric_table()
    {
        var tableId = await SeedTableAsync("T-QA");
        var otherTableId = await SeedTableAsync("T-OTHER");
        var sessionId = await SeedStableSessionAsync(tableId);
        await SeedStableOrderAsync(null, otherTableId, "T-OTHER", 20m, Utc(12, 1));

        await using var context = _fixture.CreateContext();
        var dto = await new TableServiceSessionReader(context, Assembler())
            .ReadAsync(sessionId, CancellationToken.None);

        dto.Should().NotBeNull();
        dto!.HasUnassignedActiveOrders.Should().BeFalse();
        dto.CanClose.Should().BeTrue();

        var close = await CloseAsync(sessionId, expectedVersion: 1);
        close.Success.Should().BeTrue();
        close.Data!.Status.Should().Be(nameof(TableServiceSessionStatus.Closed));
    }

    [Fact]
    public async Task Concurrent_stable_table_opens_return_the_same_authoritative_session()
    {
        var tableId = await SeedTableAsync("42");
        var results = await Task.WhenAll(OpenStableTableAsync(tableId), OpenStableTableAsync(tableId));

        results.Should().OnlyContain(result => result.Success);
        results.Select(result => result.Data!.ServiceSessionId).Distinct().Should().ContainSingle();
        await using var context = _fixture.CreateContext();
        (await context.TableServiceSessions.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Stable_table_open_rejects_missing_and_inactive_tables()
    {
        var inactiveId = await SeedTableAsync("43", isActive: false);

        await using var context = _fixture.CreateContext();
        var handler = OpenHandler(context);
        var inactive = await Assert.ThrowsAsync<BadRequestException>(() => handler.Handle(
            new OpenTableServiceSessionCommand { TableId = inactiveId }, CancellationToken.None));
        var missing = await Assert.ThrowsAsync<NotFoundException>(() => handler.Handle(
            new OpenTableServiceSessionCommand { TableId = Guid.NewGuid() }, CancellationToken.None));

        inactive.ErrorCode.Should().Be(ErrorCodes.TableServiceTableInactive);
        missing.ErrorCode.Should().Be(ErrorCodes.TableServiceTableNotFound);
    }

    [Fact]
    public async Task SessionBill_ContainsOnlyImmutableMultiRoundMembers()
    {
        var sessionId = await SeedSessionAsync(7);
        var first = await SeedOrderAsync(sessionId, 7, 30m, Utc(12, 0));
        var second = await SeedOrderAsync(sessionId, 7, 20m, Utc(12, 30));
        await SeedOrderAsync(null, 7, 99m, Utc(11, 0));

        var bill = await Assembler().AssembleAsync(sessionId, CancellationToken.None);

        bill.Should().NotBeNull();
        bill!.ServiceSessionId.Should().Be(sessionId);
        bill.Orders.Select(order => order.Id).Should().ContainInOrder(first, second);
        bill.Total.Should().Be(50m);
        bill.Remaining.Should().Be(50m);
    }

    [Fact]
    public async Task ActiveList_ExposesRoundCountOutstandingAgeAndVersion()
    {
        var sessionId = await SeedSessionAsync(13);
        await SeedOrderAsync(sessionId, 13, 17m, Utc(12, 0));

        await using var context = _fixture.CreateContext();
        var assembler = new TableBillAssembler(
            context,
            new OrderMappingService(context, new OrderDisplayCurrencyResolver(context),
                NullLogger<OrderMappingService>.Instance),
            NullLogger<TableBillAssembler>.Instance);
        var sessions = await new TableServiceSessionReader(context, assembler)
            .ReadActiveAsync(CancellationToken.None);

        sessions.Should().ContainSingle();
        var summary = sessions.Single();
        summary.ServiceSessionId.Should().Be(sessionId);
        summary.RoundCount.Should().Be(1);
        summary.Outstanding.Should().Be(17m);
        summary.Version.Should().Be(1);
        summary.AgeMinutes.Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public async Task ActiveList_AssembliesMultipleSessionsAndKeepsEmptyAndSettledBills()
    {
        var firstSessionId = await SeedSessionAsync(31, currency: "chf");
        var firstOrder = await SeedOrderAsync(firstSessionId, 31, 10m, Utc(12, 0));
        var settledOrder = await SeedOrderAsync(firstSessionId, 31, 20m, Utc(12, 30));
        await MarkCompletedAsync(settledOrder);
        var secondSessionId = await SeedSessionAsync(32, currency: "eur");
        var secondOrder = await SeedOrderAsync(secondSessionId, 32, 7m, Utc(13, 0));
        var emptySessionId = await SeedSessionAsync(33);
        var queryCounter = new SessionMetadataQueryCounter();

        await using var context = _fixture.CreateContext(queryCounter);
        var assembler = new TableBillAssembler(
            context,
            new OrderMappingService(context, new OrderDisplayCurrencyResolver(context),
                NullLogger<OrderMappingService>.Instance),
            NullLogger<TableBillAssembler>.Instance);
        var sessions = await new TableServiceSessionReader(context, assembler)
            .ReadActiveAsync(CancellationToken.None);

        queryCounter.SessionMetadataQueries.Should().Be(1);
        sessions.Select(session => session.TableNumber).Should().ContainInOrder(31, 32, 33);
        var first = sessions.Single(session => session.ServiceSessionId == firstSessionId);
        first.Currency.Should().Be("CHF");
        first.RoundCount.Should().Be(2);
        first.Outstanding.Should().Be(30m);
        first.Bill.Orders.Select(order => order.Id).Should().ContainInOrder(firstOrder, settledOrder);
        first.Bill.Total.Should().Be(30m);

        var second = sessions.Single(session => session.ServiceSessionId == secondSessionId);
        second.Currency.Should().Be("EUR");
        second.RoundCount.Should().Be(1);
        second.Bill.Orders.Select(order => order.Id).Should().ContainSingle().Which.Should().Be(secondOrder);

        var empty = sessions.Single(session => session.ServiceSessionId == emptySessionId);
        empty.RoundCount.Should().Be(0);
        empty.Outstanding.Should().Be(0m);
        empty.Bill.Orders.Should().BeEmpty();
        empty.Bill.GeneratedAt.Should().NotBe(default);
    }

    [Fact]
    public async Task Single_read_falls_back_to_the_tenant_currency_when_the_session_has_none()
    {
        var sessionId = await SeedSessionAsync(34);
        var original = await TenantCurrencyAsync();
        try
        {
            await SetTenantCurrencyAsync("CHF");

            await using var context = _fixture.CreateContext();
            var dto = await new TableServiceSessionReader(context, Assembler())
                .ReadAsync(sessionId, CancellationToken.None);

            dto.Should().NotBeNull();
            dto!.Currency.Should().Be("CHF");
            dto.Bill.Currency.Should().Be("CHF");
        }
        finally
        {
            await SetTenantCurrencyAsync(original);
        }
    }

    [Fact]
    public async Task Session_currency_wins_over_the_tenant_default()
    {
        var sessionId = await SeedSessionAsync(35, currency: "eur");
        var original = await TenantCurrencyAsync();
        try
        {
            await SetTenantCurrencyAsync("CHF");

            await using var context = _fixture.CreateContext();
            var dto = await new TableServiceSessionReader(context, Assembler())
                .ReadAsync(sessionId, CancellationToken.None);

            dto.Should().NotBeNull();
            dto!.Currency.Should().Be("EUR");
            dto.Bill.Currency.Should().Be("EUR");
        }
        finally
        {
            await SetTenantCurrencyAsync(original);
        }
    }

    [Fact]
    public async Task Active_read_falls_back_to_the_tenant_currency_only_for_sessions_without_one()
    {
        var withOwnCurrencyId = await SeedSessionAsync(36, currency: "eur");
        var withoutCurrencyId = await SeedSessionAsync(37);
        var original = await TenantCurrencyAsync();
        try
        {
            await SetTenantCurrencyAsync("CHF");

            await using var context = _fixture.CreateContext();
            var sessions = await new TableServiceSessionReader(context, Assembler())
                .ReadActiveAsync(CancellationToken.None);

            sessions.Single(session => session.ServiceSessionId == withOwnCurrencyId).Currency
                .Should().Be("EUR");
            var healed = sessions.Single(session => session.ServiceSessionId == withoutCurrencyId);
            healed.Currency.Should().Be("CHF");
            healed.Bill.Currency.Should().Be("CHF");
        }
        finally
        {
            await SetTenantCurrencyAsync(original);
        }
    }

    [Fact]
    public async Task LegacyTableBill_RefusesOldUnassignedRoundMixedWithExplicitVisit()
    {
        var sessionId = await SeedSessionAsync(8);
        await SeedOrderAsync(sessionId, 8, 20m, Utc(12, 0));
        await SeedOrderAsync(null, 8, 10m, Utc(11, 0));

        var response = await new GetTableBillQueryHandler(
                Assembler(), NullLogger<GetTableBillQueryHandler>.Instance)
            .Handle(new GetTableBillQuery(8), CancellationToken.None);

        response.Success.Should().BeFalse();
        response.ErrorCode.Should().Be(ErrorCodes.TableServiceSessionAmbiguous);
    }

    [Fact]
    public async Task Close_RefusesActiveLegacyOrdersWithoutChangingMembership()
    {
        var sessionId = await SeedSessionAsync(14);
        var legacyOrderId = await SeedOrderAsync(null, 14, 20m, Utc(12, 0));

        var result = await CloseAsync(sessionId, expectedVersion: 1);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.TableServiceSessionAmbiguous);
        result.Errors.Should().ContainSingle(TableBillTargetResolver.AmbiguousMessage);
        await using var verify = _fixture.CreateContext();
        (await verify.TableServiceSessions.SingleAsync(value => value.Id == sessionId))
            .Status.Should().Be(TableServiceSessionStatus.Open);
        (await verify.Orders.SingleAsync(value => value.Id == legacyOrderId))
            .ServiceSessionId.Should().BeNull();
    }

    [Fact]
    public async Task Close_RejectsStaleExpectedVersionWithoutWriting()
    {
        var sessionId = await SeedSessionAsync(15);

        var result = await CloseAsync(sessionId, expectedVersion: 0);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.TableServiceSessionStale);
        result.Errors.Should().ContainSingle().Which.Should().Contain("current version is 1");
        await using var verify = _fixture.CreateContext();
        (await verify.TableServiceSessions.SingleAsync(value => value.Id == sessionId))
            .Status.Should().Be(TableServiceSessionStatus.Open);
    }

    [Fact]
    public async Task ConcurrentClose_OneCommitIsReplayedByTheLoser()
    {
        var sessionId = await SeedSessionAsync(16);
        var gate = new SessionReadGate();

        var results = await Task.WhenAll(
            CloseAsync(sessionId, expectedVersion: 1, gate),
            CloseAsync(sessionId, expectedVersion: 1, gate));

        results.Should().OnlyContain(result => result.Success);
        results.Select(result => result.Data!.Status).Should().OnlyContain(
            status => status == nameof(TableServiceSessionStatus.Closed));
        await using var verify = _fixture.CreateContext();
        var session = await verify.TableServiceSessions.SingleAsync(value => value.Id == sessionId);
        session.Status.Should().Be(TableServiceSessionStatus.Closed);
        session.Version.Should().Be(2);
    }

    [Fact]
    public async Task PaymentAndClose_RaceWithOneAuthoritativeVersion()
    {
        var sessionId = await SeedSessionAsync(17);
        var gate = new SessionReadGate();

        var close = CloseAsync(sessionId, expectedVersion: 1, gate);
        var payment = PayWithWriterAsync(sessionId, expectedVersion: 1, gate);
        var results = await Task.WhenAll(close, payment);

        results.Count(result => result.Success).Should().Be(1);
        results.Should().Contain(result => result.ErrorCode == ErrorCodes.TableServiceSessionStale);
        await using var verify = _fixture.CreateContext();
        (await verify.TableServiceSessions.SingleAsync(value => value.Id == sessionId))
            .Version.Should().Be(2);
    }

    [Fact]
    public async Task Payment_RejectsStaleVersionWithoutWritingTender()
    {
        var sessionId = await SeedSessionAsync(9);
        await SeedOrderAsync(sessionId, 9, 30m, Utc(12, 0));

        var result = await PayAsync(sessionId, expectedVersion: 0, amount: 10m);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.TableServiceSessionStale);
        await using var verify = _fixture.CreateContext();
        (await verify.OrderPayments.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Payment_AllocatesPartialThenFullTenderAcrossRounds()
    {
        var sessionId = await SeedSessionAsync(10);
        var first = await SeedOrderAsync(sessionId, 10, 30m, Utc(12, 0));
        var second = await SeedOrderAsync(sessionId, 10, 20m, Utc(12, 30));

        var partial = await PayAsync(sessionId, expectedVersion: 1, amount: 40m);
        partial.Success.Should().BeTrue();
        partial.Data!.Version.Should().Be(2);
        partial.Data.Bill.Remaining.Should().Be(10m);

        var full = await PayAsync(sessionId, expectedVersion: 2, amount: 10m);
        full.Success.Should().BeTrue();
        full.Data!.Version.Should().Be(3);
        full.Data.Bill.Remaining.Should().Be(0m);

        await using var verify = _fixture.CreateContext();
        var firstRow = await verify.Orders.Include(order => order.Payments).SingleAsync(order => order.Id == first);
        var secondRow = await verify.Orders.Include(order => order.Payments).SingleAsync(order => order.Id == second);
        firstRow.TotalPaid.Should().Be(30m);
        secondRow.TotalPaid.Should().Be(20m);
        (await verify.TableBillPaymentOperations.CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task Close_RefusesOutstandingOrUnresolvedRounds_ThenClosesSafely()
    {
        var sessionId = await SeedSessionAsync(11);
        var orderId = await SeedOrderAsync(sessionId, 11, 25m, Utc(12, 0));

        var beforePayment = await CloseAsync(sessionId, expectedVersion: 1);
        beforePayment.Success.Should().BeFalse();
        beforePayment.ErrorCode.Should().Be(ErrorCodes.TableServiceSessionNotClosable);
        beforePayment.Errors.Should().Contain(error => error.Contains("outstanding"));
        beforePayment.Errors.Should().Contain(error => error.Contains("unresolved"));

        var paid = await PayAsync(sessionId, expectedVersion: 1, amount: 25m);
        paid.Success.Should().BeTrue();

        var stillUnresolved = await CloseAsync(sessionId, expectedVersion: 2);
        stillUnresolved.Success.Should().BeFalse();
        stillUnresolved.Errors.Should().Contain(error => error.Contains("unresolved"));

        await MarkCompletedAsync(orderId);
        var closed = await CloseAsync(sessionId, expectedVersion: 2);
        closed.Success.Should().BeTrue();
        closed.Data!.Status.Should().Be(nameof(TableServiceSessionStatus.Closed));
        closed.Data.Version.Should().Be(3);

        var retry = await CloseAsync(sessionId, expectedVersion: 2);
        retry.Success.Should().BeTrue();
        retry.Data!.Status.Should().Be(nameof(TableServiceSessionStatus.Closed));
    }

    [Fact]
    public async Task Payment_RejectsConflictingKnownCurrency()
    {
        var sessionId = await SeedSessionAsync(12, currency: "CHF");
        await SeedOrderAsync(sessionId, 12, 10m, Utc(12, 0));

        var result = await PayAsync(sessionId, expectedVersion: 1, amount: 1m, currency: "EUR");

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.TableServiceSessionCurrencyMismatch);
    }

    [Fact]
    public async Task SessionBill_ReportsRoundSettlementStatesAndCollectsOnlyEligibleDebt()
    {
        var sessionId = await SeedSessionAsync(18);
        var refunded = await SeedOrderAsync(sessionId, 18, 10m, Utc(12, 0));
        var eligible = await SeedOrderAsync(sessionId, 18, 20m, Utc(12, 30));
        await MarkRefundedAsync(refunded);

        var bill = await Assembler().AssembleAsync(sessionId, CancellationToken.None);

        bill.Should().NotBeNull();
        bill!.EligibleOutstanding.Should().Be(20m);
        bill.Credit.Should().Be(0m);
        bill.Rounds.Select(round => round.SettlementState).Should()
            .ContainInOrder("Refunded", "EligibleDebt");
        bill.Rounds[0].CanCollect.Should().BeFalse();
        bill.Rounds[1].CanCollect.Should().BeTrue();

        var payment = await PayAsync(sessionId, expectedVersion: 1, amount: 10m);
        payment.Success.Should().BeTrue();
        await using var verify = _fixture.CreateContext();
        var refundedRow = await verify.Orders.Include(order => order.Payments)
            .SingleAsync(order => order.Id == refunded);
        var eligibleRow = await verify.Orders.Include(order => order.Payments)
            .SingleAsync(order => order.Id == eligible);
        refundedRow.Payments.Should().ContainSingle(payment => payment.Status == PaymentStatus.Refunded);
        eligibleRow.Payments.Should().ContainSingle(payment =>
            payment.Status == PaymentStatus.Completed && payment.Amount == 10m);
    }

    [Fact]
    public async Task SessionRead_ReportsLegacyOccupancyAndCloseParity()
    {
        var sessionId = await SeedSessionAsync(19);
        await SeedOrderAsync(null, 19, 10m, Utc(12, 0));

        await using var context = _fixture.CreateContext();
        var dto = await new TableServiceSessionReader(context, Assembler()).ReadAsync(
            sessionId, CancellationToken.None);

        dto.Should().NotBeNull();
        dto!.HasUnassignedActiveOrders.Should().BeTrue();
        dto.LegacyActiveOrderCount.Should().Be(1);
        dto.CanClose.Should().BeFalse();
    }

    [Fact]
    public async Task PaymentOperationLookup_IsCommittedOnlyInsideItsOwningSession()
    {
        var sessionId = await SeedSessionAsync(20);
        await SeedOrderAsync(sessionId, 20, 10m, Utc(12, 0));
        var operationId = Guid.NewGuid();
        (await PayAsync(sessionId, expectedVersion: 1, amount: 3m, operationId: operationId))
            .Success.Should().BeTrue();
        var otherSessionId = await SeedSessionAsync(21);

        await using var context = _fixture.CreateContext();
        var mapping = new OrderMappingService(
            context, new OrderDisplayCurrencyResolver(context),
            NullLogger<OrderMappingService>.Instance);
        var assembler = new TableBillAssembler(context, mapping, NullLogger<TableBillAssembler>.Instance);
        var reader = new TableServiceSessionReader(context, assembler);
        var handler = new GetTableServiceSessionPaymentOperationQueryHandler(context, reader, mapping);

        var committed = await handler.Handle(
            new GetTableServiceSessionPaymentOperationQuery(sessionId, operationId),
            CancellationToken.None);
        committed.Data!.Status.Should().Be("Committed");
        committed.Data.Session.Should().NotBeNull();
        committed.Data.Payments.Should().ContainSingle(payment => payment.Amount == 3m);

        var crossSession = await handler.Handle(
            new GetTableServiceSessionPaymentOperationQuery(otherSessionId, operationId),
            CancellationToken.None);
        crossSession.Data!.Status.Should().Be("Unknown");
        crossSession.Data.Session.Should().BeNull();
        crossSession.Data.Payments.Should().BeEmpty();
    }

    private TableBillAssembler Assembler()
    {
        var context = _fixture.CreateContext();
        var mapping = new OrderMappingService(
            context,
            new OrderDisplayCurrencyResolver(context),
            NullLogger<OrderMappingService>.Instance);
        return new TableBillAssembler(context, mapping, NullLogger<TableBillAssembler>.Instance);
    }

    private async Task<ApiResponse<TableServiceSessionDto>> PayAsync(
        Guid sessionId, int expectedVersion, decimal amount, string? currency = null,
        DbCommandInterceptor? interceptor = null, Guid? operationId = null)
    {
        await using var context = interceptor is null
            ? _fixture.CreateContext()
            : _fixture.CreateContext(interceptor);
        var current = new Mock<ICurrentUserService>();
        current.Setup(value => value.GetAuditIdentifier()).Returns(nameof(TableServiceSessionTests));
        current.Setup(value => value.UserId).Returns(Guid.NewGuid());
        var applicator = new OrderPaymentApplicator(
            context, current.Object, new Mock<RestaurantSystem.Api.Features.FidelityPoints.Interfaces.IFidelityPointsService>().Object,
            new OrderPaymentReplayResolver(context), NullLogger<OrderPaymentApplicator>.Instance);
        var assembler = new TableBillAssembler(
            context,
            new OrderMappingService(context, new OrderDisplayCurrencyResolver(context),
                NullLogger<OrderMappingService>.Instance),
            NullLogger<TableBillAssembler>.Instance);
        var reader = new TableServiceSessionReader(context, assembler);
        var handler = new AddTableServiceSessionPaymentCommandHandler(
            context,
            new TableServiceSessionPaymentWriter(context, applicator),
            new TableServiceSessionPaymentReplayResolver(context, reader),
            reader,
            NullLogger<AddTableServiceSessionPaymentCommandHandler>.Instance);
        return await handler.Handle(new AddTableServiceSessionPaymentCommand
        {
            ServiceSessionId = sessionId,
            ExpectedVersion = expectedVersion,
            OperationId = operationId ?? Guid.NewGuid(),
            PaymentMethod = PaymentMethod.Cash,
            Amount = amount,
            Currency = currency,
        }, CancellationToken.None);
    }

    private async Task<ApiResponse<TableServiceSessionDto>> PayWithWriterAsync(
        Guid sessionId, int expectedVersion, DbCommandInterceptor interceptor)
    {
        await using var context = _fixture.CreateContext(interceptor);
        var assembler = new TableBillAssembler(
            context,
            new OrderMappingService(context, new OrderDisplayCurrencyResolver(context),
                NullLogger<OrderMappingService>.Instance),
            NullLogger<TableBillAssembler>.Instance);
        var reader = new TableServiceSessionReader(context, assembler);
        var writer = new Mock<ITableServiceSessionPaymentWriter>();
        writer.Setup(value => value.ApplyAsync(
                It.IsAny<TableServiceSession>(), It.IsAny<AddTableServiceSessionPaymentCommand>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SessionPaymentWriteResult(true, 1m, 0));
        var handler = new AddTableServiceSessionPaymentCommandHandler(
            context,
            writer.Object,
            new TableServiceSessionPaymentReplayResolver(context, reader),
            reader,
            NullLogger<AddTableServiceSessionPaymentCommandHandler>.Instance);
        return await handler.Handle(new AddTableServiceSessionPaymentCommand
        {
            ServiceSessionId = sessionId,
            ExpectedVersion = expectedVersion,
            OperationId = Guid.NewGuid(),
            PaymentMethod = PaymentMethod.Cash,
            Amount = 1m,
        }, CancellationToken.None);
    }

    private async Task<ApiResponse<TableServiceSessionDto>> CloseAsync(
        Guid sessionId, int expectedVersion, DbCommandInterceptor? interceptor = null)
    {
        await using var context = interceptor is null
            ? _fixture.CreateContext()
            : _fixture.CreateContext(interceptor);
        var assembler = new TableBillAssembler(
            context,
            new OrderMappingService(context, new OrderDisplayCurrencyResolver(context),
                NullLogger<OrderMappingService>.Instance),
            NullLogger<TableBillAssembler>.Instance);
        var reader = new TableServiceSessionReader(context, assembler);
        return await new CloseTableServiceSessionCommandHandler(context, reader).Handle(
            new CloseTableServiceSessionCommand
            {
                ServiceSessionId = sessionId,
                ExpectedVersion = expectedVersion,
            }, CancellationToken.None);
    }

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
            OpenedAt = Utc(11, 0),
            CreatedAt = Utc(11, 0),
            CreatedBy = nameof(TableServiceSessionTests),
        });
        await context.SaveChangesAsync();
        return id;
    }

    private async Task<Guid> SeedStableSessionAsync(Guid tableId)
    {
        var id = Guid.NewGuid();
        await using var context = _fixture.CreateContext();
        context.TableServiceSessions.Add(new TableServiceSession
        {
            Id = id,
            TableId = tableId,
            Status = TableServiceSessionStatus.Open,
            Version = 1,
            OpenedAt = Utc(11, 0),
            CreatedAt = Utc(11, 0),
            CreatedBy = nameof(TableServiceSessionTests),
        });
        await context.SaveChangesAsync();
        return id;
    }

    // RestaurantInfo is a Respawn-ignored singleton, so a currency set here must be restored by
    // the caller. ExecuteUpdate bypasses the audit interceptor — same restore path the
    // RestaurantInfoSeederTests use — so the row's UpdatedAt stays pristine.
    private async Task<string?> TenantCurrencyAsync()
    {
        await using var context = _fixture.CreateContext();
        return await context.RestaurantInfo.Select(info => info.Currency).SingleAsync();
    }

    private async Task SetTenantCurrencyAsync(string? currency)
    {
        await using var context = _fixture.CreateContext();
        await context.RestaurantInfo.ExecuteUpdateAsync(setters =>
            setters.SetProperty(info => info.Currency, currency));
    }

    private async Task<Guid> SeedOrderAsync(Guid? sessionId, int table, decimal total, DateTime orderedAt)
    {
        var id = Guid.NewGuid();
        await using var context = _fixture.CreateContext();
        context.Orders.Add(new Order
        {
            Id = id,
            OrderNumber = $"TS-{id:N}"[..12],
            Type = OrderType.DineIn,
            TableNumber = table,
            ServiceSessionId = sessionId,
            Status = OrderStatus.Confirmed,
            PaymentStatus = PaymentStatus.Pending,
            SubTotal = total,
            Total = total,
            RemainingAmount = total,
            OrderDate = orderedAt,
            CreatedAt = orderedAt,
            CreatedBy = nameof(TableServiceSessionTests),
        });
        await context.SaveChangesAsync();
        return id;
    }

    private async Task<Guid> SeedStableOrderAsync(
        Guid? sessionId, Guid tableId, string label, decimal total, DateTime orderedAt)
    {
        var id = Guid.NewGuid();
        await using var context = _fixture.CreateContext();
        context.Orders.Add(new Order
        {
            Id = id,
            OrderNumber = $"TS-{id:N}"[..12],
            Type = OrderType.DineIn,
            TableId = tableId,
            TableLabel = label,
            ServiceSessionId = sessionId,
            Status = OrderStatus.Confirmed,
            PaymentStatus = PaymentStatus.Pending,
            SubTotal = total,
            Total = total,
            RemainingAmount = total,
            OrderDate = orderedAt,
            CreatedAt = orderedAt,
            CreatedBy = nameof(TableServiceSessionTests),
        });
        await context.SaveChangesAsync();
        return id;
    }

    private async Task MarkRefundedAsync(Guid orderId)
    {
        await using var context = _fixture.CreateContext();
        var order = await context.Orders.SingleAsync(value => value.Id == orderId);
        var now = Utc(13, 0);
        order.Status = OrderStatus.Completed;
        order.PaymentStatus = PaymentStatus.Refunded;
        order.TotalPaid = 0m;
        order.RemainingAmount = order.Total;
        context.OrderPayments.Add(new OrderPayment
        {
            OrderId = order.Id,
            PaymentMethod = PaymentMethod.Cash,
            Amount = order.Total,
            Status = PaymentStatus.Refunded,
            IsRefunded = true,
            RefundedAmount = order.Total,
            PaymentDate = now,
            RefundDate = now,
            CreatedAt = now,
            CreatedBy = nameof(TableServiceSessionTests),
        });
        await context.SaveChangesAsync();
    }

    private async Task MarkCompletedAsync(Guid orderId)
    {
        await using var context = _fixture.CreateContext();
        var order = await context.Orders.SingleAsync(value => value.Id == orderId);
        order.Status = OrderStatus.Completed;
        await context.SaveChangesAsync();
    }

    private async Task<Guid> SeedTableAsync(string label, bool isActive = true)
    {
        var id = Guid.NewGuid();
        await using var context = _fixture.CreateContext();
        context.Tables.Add(new Table
        {
            Id = id,
            TableNumber = label,
            MaxGuests = 4,
            IsActive = isActive,
            CreatedAt = Utc(10, 0),
            CreatedBy = nameof(TableServiceSessionTests),
        });
        await context.SaveChangesAsync();
        return id;
    }

    private async Task<ApiResponse<TableServiceSessionDto>> OpenStableTableAsync(Guid tableId)
    {
        await using var context = _fixture.CreateContext();
        return await OpenHandler(context).Handle(
            new OpenTableServiceSessionCommand { TableId = tableId }, CancellationToken.None);
    }

    private OpenTableServiceSessionCommandHandler OpenHandler(ApplicationDbContext context)
    {
        var mapping = new OrderMappingService(
            context, new OrderDisplayCurrencyResolver(context),
            NullLogger<OrderMappingService>.Instance);
        var assembler = new TableBillAssembler(
            context, mapping, NullLogger<TableBillAssembler>.Instance);
        var current = new Mock<ICurrentUserService>();
        current.Setup(value => value.GetAuditIdentifier()).Returns(nameof(TableServiceSessionTests));
        current.Setup(value => value.UserId).Returns(Guid.NewGuid());
        return new OpenTableServiceSessionCommandHandler(
            context,
            current.Object,
            new TableServiceSessionReader(context, assembler),
            new TableIdentityResolver(context));
    }

    private static DateTime Utc(int hour, int minute) => new(2026, 9, 11, hour, minute, 0, DateTimeKind.Utc);

    private sealed class SessionMetadataQueryCounter : DbCommandInterceptor
    {
        private int _sessionMetadataQueries;

        public int SessionMetadataQueries => Volatile.Read(ref _sessionMetadataQueries);

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("table_service_sessions", StringComparison.OrdinalIgnoreCase))
            {
                Interlocked.Increment(ref _sessionMetadataQueries);
            }

            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }

    private sealed class SessionReadGate : DbCommandInterceptor
    {
        private readonly TaskCompletionSource<bool> _bothArrived =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _arrivals;

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("table_service_sessions", StringComparison.OrdinalIgnoreCase)
                && Interlocked.Increment(ref _arrivals) <= 2)
            {
                if (Volatile.Read(ref _arrivals) == 2)
                {
                    _bothArrived.TrySetResult(true);
                }

                await _bothArrived.Task.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
            }

            return await base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }
}
