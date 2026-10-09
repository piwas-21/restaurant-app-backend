using FluentAssertions;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

[Collection("Database Lane 2")]
public sealed class TableBillPaymentTipReaderTests : IAsyncLifetime
{
    private readonly DatabaseFixture _fixture;

    public TableBillPaymentTipReaderTests(DatabaseFixture fixture) => _fixture = fixture;

    public Task InitializeAsync() => _fixture.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Legacy_order_tip_survives_session_repair_and_is_net_of_tip_refunds()
    {
        var tableNumber = 72;
        var now = DateTime.UtcNow;
        var sessionId = Guid.NewGuid();
        await using var context = _fixture.CreateContext();
        var order = CreateOrder(tableNumber, now);
        var ordinaryTender = CreateTender(order, 300, now);
        var completedUnrepairedOrder = CreateOrder(tableNumber, now);
        var legacyTableOperation = CreateTableOperation(tableNumber, 450, now);
        var firstAllocation = CreateTender(order, 125, now, legacyTableOperation);
        var secondAllocation = CreateTender(completedUnrepairedOrder, 175, now, legacyTableOperation);
        order.Payments.Add(ordinaryTender);
        context.Orders.AddRange(order, completedUnrepairedOrder);
        context.TableBillPaymentOperations.Add(legacyTableOperation);
        context.OrderPayments.AddRange(firstAllocation, secondAllocation);
        await context.SaveChangesAsync();

        (await TableBillPaymentTipReader.ReadAsync(context, null, tableNumber, CancellationToken.None))
            .Should().Be(7.5m, "the legacy bill includes both tips and counts its split operation once");

        var session = new TableServiceSession
        {
            Id = sessionId,
            TableNumber = tableNumber,
            Currency = "CHF",
            OpenedAt = now,
            CreatedAt = now,
            CreatedBy = nameof(TableBillPaymentTipReaderTests)
        };
        context.TableServiceSessions.Add(session);
        order.ServiceSession = session;
        await context.SaveChangesAsync();

        var afterRepair = await TableBillPaymentTipReader.ReadManyAsync(context, [sessionId], CancellationToken.None);
        afterRepair[sessionId].Should().Be(7.5m,
            "the operation follows its unique repaired allocation even while another order remains legacy");
        (await TableBillPaymentTipReader.ReadAsync(context, sessionId, tableNumber, CancellationToken.None))
            .Should().Be(7.5m, "single-session reads attribute the legacy operation through its order links");
        (await TableBillPaymentTipReader.ReadAsync(context, null, tableNumber, CancellationToken.None))
            .Should().Be(0m, "the old table-number view no longer leaks an operation assigned to this visit");

        ordinaryTender.RefundedTipMinor = 100;
        ordinaryTender.Status = PaymentStatus.PartiallyRefunded;
        await context.SaveChangesAsync();
        var afterRefund = await TableBillPaymentTipReader.ReadManyAsync(context, [sessionId], CancellationToken.None);
        afterRefund[sessionId].Should().Be(6.5m, "the ordinary tender tip is net of its refund");

        var tableOperation = CreateTableOperation(tableNumber, 125, now, sessionId);
        context.TableBillPaymentOperations.Add(tableOperation);
        context.OrderPayments.Add(CreateTender(order, 0, now, tableOperation));
        await context.SaveChangesAsync();

        var withTableTender = await TableBillPaymentTipReader.ReadManyAsync(context, [sessionId], CancellationToken.None);
        withTableTender[sessionId].Should().Be(7.75m,
            "the explicit operation, legacy operation, and ordinary order tip each count exactly once");
    }

    [Fact]
    public async Task Legacy_operation_tip_requires_one_non_null_session_and_explicit_session_wins()
    {
        var now = DateTime.UtcNow;
        const int firstTable = 72;
        const int secondTable = 73;
        var firstSessionId = Guid.NewGuid();
        var secondSessionId = Guid.NewGuid();
        await using var context = _fixture.CreateContext();
        var firstSession = CreateSession(firstTable, firstSessionId, now);
        var secondSession = CreateSession(secondTable, secondSessionId, now);
        var firstOrder = CreateOrder(firstTable, now);
        var firstOrderDuplicateAllocation = CreateOrder(firstTable, now);
        var secondOrder = CreateOrder(secondTable, now);
        var unassignedOrder = CreateOrder(firstTable, now);
        firstOrder.ServiceSession = firstSession;
        firstOrderDuplicateAllocation.ServiceSession = firstSession;
        secondOrder.ServiceSession = secondSession;

        var uniqueLegacyOperation = CreateTableOperation(firstTable, 500, now);
        var ambiguousLegacyOperation = CreateTableOperation(firstTable, 700, now);
        var unassignedLegacyOperation = CreateTableOperation(firstTable, 300, now);
        var explicitOperation = CreateTableOperation(firstTable, 125, now, firstSessionId);
        context.TableServiceSessions.AddRange(firstSession, secondSession);
        context.Orders.AddRange(firstOrder, firstOrderDuplicateAllocation, secondOrder, unassignedOrder);
        context.TableBillPaymentOperations.AddRange(
            uniqueLegacyOperation, ambiguousLegacyOperation, unassignedLegacyOperation, explicitOperation);
        context.OrderPayments.AddRange(
            CreateTender(firstOrder, 0, now, uniqueLegacyOperation),
            CreateTender(firstOrderDuplicateAllocation, 0, now, uniqueLegacyOperation),
            CreateTender(firstOrder, 0, now, ambiguousLegacyOperation),
            CreateTender(secondOrder, 0, now, ambiguousLegacyOperation),
            CreateTender(unassignedOrder, 0, now, unassignedLegacyOperation),
            CreateTender(secondOrder, 0, now, explicitOperation));
        await context.SaveChangesAsync();

        var sessionTips = await TableBillPaymentTipReader.ReadManyAsync(
            context, [firstSessionId, secondSessionId], CancellationToken.None);
        sessionTips[firstSessionId].Should().Be(6.25m,
            "the unique legacy operation is counted once and the explicit operation follows its session field");
        sessionTips.GetValueOrDefault(secondSessionId).Should().Be(0m,
            "an ambiguous operation and a cross-linked explicit operation are not assigned to this visit");
        (await TableBillPaymentTipReader.ReadAsync(context, secondSessionId, secondTable, CancellationToken.None))
            .Should().Be(0m, "the cross-linked explicit operation cannot leak across visits");
        (await TableBillPaymentTipReader.ReadAsync(context, null, firstTable, CancellationToken.None))
            .Should().Be(10m,
                "ambiguous and unassigned legacy tips remain in legacy scope while the unique visit tip moves out");
    }

    private static TableServiceSession CreateSession(int tableNumber, Guid sessionId, DateTime now) => new()
    {
        Id = sessionId,
        TableNumber = tableNumber,
        Currency = "CHF",
        OpenedAt = now,
        CreatedAt = now,
        CreatedBy = nameof(TableBillPaymentTipReaderTests)
    };

    private static Order CreateOrder(int tableNumber, DateTime now) => new()
    {
        Id = Guid.NewGuid(),
        OrderNumber = $"TIP-{Guid.NewGuid():N}"[..15],
        Type = OrderType.DineIn,
        TableNumber = tableNumber,
        Status = OrderStatus.Completed,
        PaymentStatus = PaymentStatus.Completed,
        OrderDate = now,
        Total = 20m,
        TotalPaid = 20m,
        CreatedAt = now,
        CreatedBy = nameof(TableBillPaymentTipReaderTests)
    };

    private static OrderPayment CreateTender(
        Order order, long tipMinor, DateTime now, TableBillPaymentOperation? tableOperation = null) => new()
        {
            Id = Guid.NewGuid(),
            Order = order,
            TableBillPaymentOperation = tableOperation,
            PaymentMethod = PaymentMethod.Cash,
            Amount = 10m,
            Status = PaymentStatus.Completed,
            TipMinor = tipMinor,
            PaymentDate = now,
            CreatedAt = now,
            CreatedBy = nameof(TableBillPaymentTipReaderTests)
        };

    private static TableBillPaymentOperation CreateTableOperation(
        int tableNumber, long tipMinor, DateTime now, Guid? sessionId = null) => new()
        {
            Id = Guid.NewGuid(),
            OperationId = Guid.NewGuid(),
            ServiceSessionId = sessionId,
            TableNumber = tableNumber,
            PaymentMethod = PaymentMethod.Cash,
            Amount = 10m,
            TipMinor = tipMinor,
            Currency = "CHF",
            CreatedAt = now,
            CreatedBy = nameof(TableBillPaymentTipReaderTests)
        };
}
