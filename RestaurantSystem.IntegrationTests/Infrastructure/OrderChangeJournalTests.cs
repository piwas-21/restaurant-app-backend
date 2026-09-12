using FluentAssertions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Features.Orders.Queries.GetOrdersQuery;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.IntegrationTests.Infrastructure;

/// <summary>Proves the database trigger, rather than one HTTP handler, owns the order change feed.</summary>
[Collection("Database Lane 1")]
public sealed class OrderChangeJournalTests : IntegrationTestBase
{
    public OrderChangeJournalTests(DatabaseFixture databaseFixture)
        : base(databaseFixture)
    {
    }

    [Fact]
    public async Task ChildUpdateAndDelete_AdvanceTheSameOrderJournal()
    {
        await using var context = DatabaseFixture.CreateContext();
        var order = NewOrder();
        context.Orders.Add(order);
        await context.SaveChangesAsync();

        var payment = new OrderPayment
        {
            OrderId = order.Id,
            PaymentMethod = PaymentMethod.Cash,
            Amount = 10m,
            Status = PaymentStatus.Completed,
            PaymentDate = DateTime.UtcNow,
            CreatedBy = "queue-test"
        };
        context.OrderPayments.Add(payment);
        await context.SaveChangesAsync();

        var afterInsert = await ReadOrderAsync(order.Id);
        var insertSequence = afterInsert.LastChangeSequence;
        insertSequence.Should().BeGreaterThan(0);

        payment.ReferenceNumber = "updated";
        await context.SaveChangesAsync();

        var afterUpdate = await ReadOrderAsync(order.Id);
        afterUpdate.LastChangeSequence.Should().BeGreaterThan(insertSequence);

        context.OrderPayments.Remove(payment);
        await context.SaveChangesAsync();

        var afterDelete = await ReadOrderAsync(order.Id);
        afterDelete.LastChangeSequence.Should().BeGreaterThan(afterUpdate.LastChangeSequence);

        await using var verification = DatabaseFixture.CreateContext();
        var changes = await verification.OrderChanges
            .Where(change => change.OrderId == order.Id)
            .OrderBy(change => change.Sequence)
            .ToListAsync();

        changes.Should().HaveCountGreaterThanOrEqualTo(4,
            "one aggregate save may persist several independently journaled child facts");
        changes.Select(change => change.Kind)
            .Should().OnlyContain(kind => kind == OrderChangeKind.Upsert);
        changes.Select(change => change.Sequence)
            .Should().BeInAscendingOrder();
        changes.Select(change => change.Sequence).Should().Contain([
            insertSequence,
            afterUpdate.LastChangeSequence,
            afterDelete.LastChangeSequence
        ]);
    }

    [Fact]
    public async Task ConcurrentWriters_CannotPassAnUncommittedWatermark()
    {
        await using var first = DatabaseFixture.CreateContext();
        await using var firstTransaction = await first.Database.BeginTransactionAsync();
        var firstOrder = NewOrder();
        first.Orders.Add(firstOrder);
        await first.SaveChangesAsync();
        var firstSequence = firstOrder.LastChangeSequence;

        await using var second = DatabaseFixture.CreateContext();
        await using var secondTransaction = await second.Database.BeginTransactionAsync();
        var lockAvailable = await second.Database
            .SqlQueryRaw<bool>(
                "SELECT pg_try_advisory_xact_lock(hashtextextended('restaurant-system.order-change-sequence', 0)) AS \"Value\"")
            .SingleAsync();
        lockAvailable.Should().BeFalse("the first transaction holds the allocator lock");

        var secondOrder = NewOrder();
        second.Orders.Add(secondOrder);
        var secondSave = second.SaveChangesAsync();
        await Task.Yield();
        secondSave.IsCompleted.Should().BeFalse("the second writer must wait for the first transaction");

        await firstTransaction.CommitAsync();
        await secondSave;
        await secondTransaction.CommitAsync();

        secondOrder.LastChangeSequence.Should().BeGreaterThan(firstSequence);
        await using var verification = DatabaseFixture.CreateContext();
        var committed = await verification.OrderChanges
            .Where(change => change.OrderId == firstOrder.Id || change.OrderId == secondOrder.Id)
            .OrderBy(change => change.Sequence)
            .Select(change => change.Sequence)
            .ToListAsync();
        committed.Should().Equal(firstSequence, secondOrder.LastChangeSequence);
    }

    [Fact]
    public async Task SnapshotPages_KeepTheirUpperWatermark()
    {
        await using var context = DatabaseFixture.CreateContext();
        var first = NewOrder(DateTime.UtcNow.AddMinutes(-2));
        var second = NewOrder(DateTime.UtcNow.AddMinutes(-1));
        context.Orders.AddRange(first, second);
        await context.SaveChangesAsync();

        var (reader, cursor, caller) = CreateReader(context);
        var query = SyncQuery(pageSize: 1);
        var filterHash = OperationalOrderQueryBuilder.FilterHash(query, caller.Object);
        var pageOne = await reader.ReadSnapshotAsync(query, filterHash, CancellationToken.None);
        var snapshotCursor = pageOne.Data!.Sync!.NextCursor;
        snapshotCursor.Should().NotBeNull();

        // This row is newer than the captured watermark and must not leak onto page two.
        var late = NewOrder(DateTime.UtcNow);
        context.Orders.Add(late);
        await context.SaveChangesAsync();

        var pageTwo = await reader.ReadSnapshotNextAsync(
            query, cursor.Read(snapshotCursor), CancellationToken.None);

        pageTwo.Data!.Items.Should().ContainSingle(item => item.Id == second.Id);
        pageTwo.Data.Items.Should().NotContain(item => item.Id == late.Id);
        pageTwo.Data.TotalCount.Should().Be(2);
    }

    [Fact]
    public async Task LaterOffsetPage_DoesNotCreateAnIncompleteSyncWatermark()
    {
        await using var context = DatabaseFixture.CreateContext();
        var first = NewOrder(DateTime.UtcNow.AddMinutes(-2));
        first.OrderNumber = "Q-OFFSET-01";
        var second = NewOrder(DateTime.UtcNow.AddMinutes(-1));
        second.OrderNumber = "Q-OFFSET-02";
        context.Orders.AddRange(first, second);
        await context.SaveChangesAsync();

        var (handler, _) = CreateHandler(context);
        var result = await handler.Handle(
            SyncQuery(pageSize: 1) with { Page = 2, Search = "Q-OFFSET" },
            CancellationToken.None);

        result.Data!.Page.Should().Be(2);
        result.Data.Items.Should().ContainSingle(item => item.Id == second.Id);
        result.Data.Sync.Should().BeNull(
            "an offset page that omitted page one cannot establish a complete snapshot");
    }

    [Fact]
    public async Task ChangesPages_UpsertCurrentRows_AndRemoveRowsThatLeaveTheFilter()
    {
        await using var context = DatabaseFixture.CreateContext();
        var order = NewOrder(DateTime.UtcNow);
        context.Orders.Add(order);
        await context.SaveChangesAsync();

        var (reader, cursor, caller) = CreateReader(context);
        var query = SyncQuery(pageSize: 10);
        var filterHash = OperationalOrderQueryBuilder.FilterHash(query, caller.Object);
        var snapshot = await reader.ReadSnapshotAsync(query, filterHash, CancellationToken.None);
        var watermark = snapshot.Data!.Sync!.Watermark;
        watermark.Should().NotBeNull();

        order.Notes = "changed while still operational";
        await context.SaveChangesAsync();
        var upsertPage = await reader.ReadChangesAsync(
            query, cursor.Read(watermark), CancellationToken.None);
        upsertPage.Data!.Items.Should().ContainSingle(item => item.Id == order.Id);
        upsertPage.Data.Sync!.Watermark.Should().NotBeNull();

        order.Status = OrderStatus.Completed;
        order.TotalPaid = order.Total;
        order.RemainingAmount = 0m;
        await context.SaveChangesAsync();
        var removalPage = await reader.ReadChangesAsync(
            query, cursor.Read(upsertPage.Data.Sync.Watermark), CancellationToken.None);

        removalPage.Data!.Items.Should().BeEmpty();
        removalPage.Data.Sync!.Removals.Should().ContainSingle(removal => removal.OrderId == order.Id);
    }

    [Fact]
    public async Task RolledBackChildMutation_LeavesNoJournalRow()
    {
        await using var context = DatabaseFixture.CreateContext();
        var order = NewOrder();
        context.Orders.Add(order);
        await context.SaveChangesAsync();

        await using (var transaction = await context.Database.BeginTransactionAsync())
        {
            context.OrderPayments.Add(new OrderPayment
            {
                OrderId = order.Id,
                PaymentMethod = PaymentMethod.Cash,
                Amount = 10m,
                Status = PaymentStatus.Completed,
                PaymentDate = DateTime.UtcNow,
                CreatedBy = "queue-test"
            });
            await context.SaveChangesAsync();
            await transaction.RollbackAsync();
        }

        await using var verification = DatabaseFixture.CreateContext();
        (await verification.OrderPayments.AnyAsync(payment => payment.OrderId == order.Id))
            .Should().BeFalse();
        (await verification.OrderChanges.CountAsync(change => change.OrderId == order.Id))
            .Should().Be(1);
    }

    private async Task<Order> ReadOrderAsync(Guid orderId)
    {
        await using var context = DatabaseFixture.CreateContext();
        return await context.Orders.SingleAsync(order => order.Id == orderId);
    }

    private static (GetOrdersQueryHandler Handler, OperationalQueueCursor Cursor) CreateHandler(
        ApplicationDbContext context)
    {
        var caller = new Mock<ICurrentUserService>();
        caller.SetupGet(user => user.IsStaff).Returns(true);
        caller.SetupGet(user => user.UserId).Returns((Guid?)null);
        var clock = new Mock<ITenantClock>();
        clock.SetupGet(value => value.TimeZone).Returns(TimeZoneInfo.Utc);
        var mapping = new Mock<IOrderMappingService>();
        mapping.Setup(value => value.MapToOrderDto(It.IsAny<Order>()))
            .Returns((Order value) => new OrderDto { Id = value.Id });
        mapping.Setup(value => value.MapToOrderDtoAsync(
                It.IsAny<Order>(), It.IsAny<CancellationToken>()))
            .Returns((Order value, CancellationToken _) =>
                Task.FromResult(new OrderDto { Id = value.Id }));
        var cursor = new OperationalQueueCursor(
            new EphemeralDataProtectionProvider(NullLoggerFactory.Instance),
            Microsoft.Extensions.Options.Options.Create(new RestaurantSystem.Api.Settings.OperationalQueueSyncOptions
            {
                TenantKey = "journal-test",
                CursorLifetime = TimeSpan.FromMinutes(15)
            }),
            TimeProvider.System);
        var reader = new OperationalQueueSyncReader(
            context, caller.Object, clock.Object, mapping.Object, cursor,
            NullLogger<OperationalQueueSyncReader>.Instance);
        return (new GetOrdersQueryHandler(
            context, caller.Object, clock.Object, mapping.Object, cursor, reader,
            NullLogger<GetOrdersQueryHandler>.Instance), cursor);
    }

    private static (OperationalQueueSyncReader Reader, OperationalQueueCursor Cursor, Mock<ICurrentUserService> Caller)
        CreateReader(ApplicationDbContext context)
    {
        var caller = new Mock<ICurrentUserService>();
        caller.SetupGet(user => user.IsStaff).Returns(true);
        caller.SetupGet(user => user.UserId).Returns((Guid?)null);

        var clock = new Mock<ITenantClock>();
        clock.SetupGet(value => value.TimeZone).Returns(TimeZoneInfo.Utc);
        clock.SetupGet(value => value.Now).Returns(DateTimeOffset.UtcNow);

        var mapping = new Mock<IOrderMappingService>();
        mapping.Setup(value => value.MapToOrderDto(It.IsAny<Order>()))
            .Returns((Order value) => new OrderDto { Id = value.Id });
        mapping.Setup(value => value.MapToOrderDtoAsync(
                It.IsAny<Order>(), It.IsAny<CancellationToken>()))
            .Returns((Order value, CancellationToken _) =>
                Task.FromResult(new OrderDto { Id = value.Id }));

        var cursor = new OperationalQueueCursor(
            new EphemeralDataProtectionProvider(NullLoggerFactory.Instance),
            Microsoft.Extensions.Options.Options.Create(new RestaurantSystem.Api.Settings.OperationalQueueSyncOptions
            {
                TenantKey = "journal-test",
                CursorLifetime = TimeSpan.FromMinutes(15)
            }),
            TimeProvider.System);
        var reader = new OperationalQueueSyncReader(
            context,
            caller.Object,
            clock.Object,
            mapping.Object,
            cursor,
            NullLogger<OperationalQueueSyncReader>.Instance);
        return (reader, cursor, caller);
    }

    private static GetOrdersQuery SyncQuery(int pageSize) => new(
        Status: null,
        PaymentStatus: null,
        OrderType: null,
        StartDate: null,
        EndDate: null,
        UserId: null,
        Search: null,
        IsFocusOrder: null,
        OrderBy: "OrderDate",
        Descending: false,
        Page: 1,
        PageSize: pageSize,
        Scope: OrderListScope.Operational);

    private static Order NewOrder(DateTime? orderDate = null) => new()
    {
        OrderNumber = $"Q-{Guid.NewGuid():N}"[..20],
        OrderDate = orderDate ?? DateTime.UtcNow,
        Status = OrderStatus.Pending,
        PaymentStatus = PaymentStatus.Pending,
        Total = 10m,
        RemainingAmount = 10m,
        CreatedBy = "queue-test"
    };
}
