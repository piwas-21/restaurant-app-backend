using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.FidelityPoints.Interfaces;
using RestaurantSystem.Api.Features.Orders.Commands.AddTableBillPaymentCommand;
using RestaurantSystem.Api.Features.Orders.Queries.GetTableBillQuery;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using Npgsql;
using RestaurantSystem.IntegrationTests.Common;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

/// <summary>
/// Handler tests for the one-bill-per-table feature, driven against a real
/// <see cref="ApplicationDbContext"/>: a table's bill is the UNION of its open
/// orders (guests order in rounds), and one bill tender spreads across those
/// orders oldest-first.
/// </summary>
[Collection("Database Lane 3")]
public class TableBillTests : IAsyncLifetime
{
    private readonly DatabaseFixture _fixture;

    public TableBillTests(DatabaseFixture fixture)
    {
        _fixture = fixture ?? throw new ArgumentNullException(nameof(fixture));
    }

    public Task InitializeAsync() => _fixture.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    // ---------------------------------------------------------------------
    // Bill assembly
    // ---------------------------------------------------------------------

    [Fact]
    public async Task Bill_IsTheUnionOfOpenOrdersOldestFirst()
    {
        // Three rounds on table 7, plus the decoys: a completed round (cleared),
        // a cancelled one, and a takeaway that merely carries the table number
        // for delivery notes. Only the two open DineIn rounds may bill.
        var round1 = await SeedDineInOrderAsync(tableNumber: 7, total: 30m, orderedAt: Utc(12, 0));
        await SeedDineInOrderAsync(tableNumber: 7, total: 20m, orderedAt: Utc(12, 45));
        await SeedDineInOrderAsync(tableNumber: 7, total: 99m, orderedAt: Utc(11, 0), status: OrderStatus.Completed);
        await SeedDineInOrderAsync(tableNumber: 7, total: 88m, orderedAt: Utc(11, 30), status: OrderStatus.Cancelled);
        await SeedTakeawayOrderAsync(total: 77m);

        var bill = await GetBillAsync(7);

        bill.Should().NotBeNull();
        bill!.OrderCount.Should().Be(2, "two open DineIn rounds — completed/cancelled/takeaway never bill");
        bill.Orders.Single(o => o.Id == round1).OrderDate.Should().Be(
            Utc(12, 0), "the bill reads in the order the table ordered");
        bill.Total.Should().Be(50m);
        bill.Remaining.Should().Be(50m);
        bill.TotalPaid.Should().Be(0m);
    }

    [Fact]
    public async Task Bill_IncludesItemsGroupedPerOrder()
    {
        var order = await SeedDineInOrderAsync(tableNumber: 5, total: 25.50m, orderedAt: Utc(19, 0));
        await SeedItemAsync(order, "Wiener Schnitzel", quantity: 2, unitPrice: 12.75m);

        var bill = await GetBillAsync(5);

        bill!.Orders.Should().ContainSingle();
        var lines = bill.Orders.Single().Items;
        lines.Should().ContainSingle()
            .Which.Should().Match<RestaurantSystem.Api.Features.Orders.Dtos.OrderItemDto>(
                i => i.ProductName == "Wiener Schnitzel" && i.Quantity == 2 && i.ItemTotal == 25.50m);
    }

    [Fact]
    public async Task Bill_ClampsAnOverpaidOrderRatherThanOffsettingItsSiblings()
    {
        // Round A was overpaid by 5 (a tender that exceeded its total); round B still
        // owes 20. The bill's remaining must read 20 — the overpayment may not hide
        // the sibling's balance, and a bill tender never reaches back into round A.
        var overpaid = await SeedDineInOrderAsync(tableNumber: 9, total: 30m, orderedAt: Utc(18, 0));
        await SettleAsync(overpaid, amount: 35m);
        await SeedDineInOrderAsync(tableNumber: 9, total: 20m, orderedAt: Utc(18, 30));

        var bill = await GetBillAsync(9);

        bill!.TotalPaid.Should().Be(35m);
        bill.Remaining.Should().Be(20m, "the -5 overpayment stays on its own order");
    }

    [Fact]
    public async Task Bill_WithNoOpenOrders_Fails()
    {
        var response = await new GetTableBillQueryHandler(Assembler(), NullLogger<GetTableBillQueryHandler>.Instance)
            .Handle(new GetTableBillQuery(42), CancellationToken.None);

        response.Success.Should().BeFalse();
        response.Errors.Should().Contain(e => e.Contains("42"));
    }

    // ---------------------------------------------------------------------
    // One tender for the whole bill
    // ---------------------------------------------------------------------

    [Fact]
    public async Task BillPayment_SpreadsAcrossOpenOrdersOldestFirst()
    {
        // Table 4: round A owes 30 (placed 12:00), round B owes 20 (12:45).
        // One tender of 40: A takes 30, B takes the remaining 10.
        var a = await SeedDineInOrderAsync(tableNumber: 4, total: 30m, orderedAt: Utc(12, 0));
        var b = await SeedDineInOrderAsync(tableNumber: 4, total: 20m, orderedAt: Utc(12, 45));

        var response = await PayBillAsync(4, amount: 40m);

        response.Success.Should().BeTrue();

        await using var ctx = _fixture.CreateContext();
        var orderA = await ctx.Orders.Include(o => o.Payments).SingleAsync(o => o.Id == a);
        var orderB = await ctx.Orders.Include(o => o.Payments).SingleAsync(o => o.Id == b);

        orderA.TotalPaid.Should().Be(30m);
        orderA.RemainingAmount.Should().Be(0m);
        orderA.PaymentStatus.Should().Be(PaymentStatus.Completed);
        orderB.TotalPaid.Should().Be(10m, "the tender spilled onto the second-oldest round");
        orderB.RemainingAmount.Should().Be(10m);
        orderB.PaymentStatus.Should().Be(PaymentStatus.PartiallyPaid);

        // The response is the POST-payment bill, so the till sees the updated balance.
        response.Data!.Remaining.Should().Be(10m);
        response.Data.TotalPaid.Should().Be(40m);
    }

    [Fact]
    public async Task BillPayment_RejectsOverpaymentAndWritesNothing()
    {
        await SeedDineInOrderAsync(tableNumber: 3, total: 30m, orderedAt: Utc(12, 0));

        var response = await PayBillAsync(3, amount: 30.02m);

        response.Success.Should().BeFalse();
        response.Errors.Should().ContainSingle().Which.Should().Contain("remaining balance");

        // A refusal that still wrote money is the false-ledger failure mode.
        await using var ctx = _fixture.CreateContext();
        ctx.OrderPayments.Should().BeEmpty();
    }

    [Fact]
    public async Task BillPayment_OnAFullySettledBill_IsRefused()
    {
        var order = await SeedDineInOrderAsync(tableNumber: 2, total: 30m, orderedAt: Utc(12, 0));
        await SettleAsync(order, amount: 30m);

        var response = await PayBillAsync(2, amount: 1m);

        response.Success.Should().BeFalse();
        response.Errors.Should().ContainSingle().Which.Should().Contain("no outstanding balance");
    }

    [Fact]
    public async Task BillPayment_WithNoOpenOrders_IsRefused()
    {
        await SeedDineInOrderAsync(tableNumber: 1, total: 30m, orderedAt: Utc(12, 0), status: OrderStatus.Completed);

        var response = await PayBillAsync(1, amount: 10m);

        response.Success.Should().BeFalse();
        response.Errors.Should().ContainSingle().Which.Should().Contain("No open orders");
    }

    // ---------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------

    private static DateTime Utc(int hour, int minute) => new(2026, 9, 10, hour, minute, 0, DateTimeKind.Utc);

    private TableBillAssembler Assembler()
    {
        var ctx = _fixture.CreateContext();
        return new TableBillAssembler(
            ctx,
            new RestaurantSystem.Api.Features.Orders.Services.OrderMappingService(
                ctx,
                new RestaurantSystem.Api.Features.Orders.Services.OrderDisplayCurrencyResolver(ctx),
                NullLogger<RestaurantSystem.Api.Features.Orders.Services.OrderMappingService>.Instance),
            NullLogger<TableBillAssembler>.Instance);
    }

    private async Task<RestaurantSystem.Api.Features.Orders.Dtos.TableBillDto?> GetBillAsync(int tableNumber)
    {
        var response = await new GetTableBillQueryHandler(Assembler(), NullLogger<GetTableBillQueryHandler>.Instance)
            .Handle(new GetTableBillQuery(tableNumber), CancellationToken.None);
        response.Success.Should().BeTrue(response.Errors is { Count: > 0 } ? string.Join("; ", response.Errors) : "");
        return response.Data;
    }

    private async Task<RestaurantSystem.Api.Common.Models.ApiResponse<RestaurantSystem.Api.Features.Orders.Dtos.TableBillDto>>
        PayBillAsync(int tableNumber, decimal amount)
    {
        var ctx = _fixture.CreateContext();
        var currentUser = new Mock<ICurrentUserService>();
        var userId = Guid.NewGuid();
        currentUser.Setup(x => x.UserId).Returns(userId);
        currentUser.Setup(x => x.GetAuditIdentifier()).Returns(userId.ToString());

        var fidelity = new Mock<IFidelityPointsService>();

        var applicator = new OrderPaymentApplicator(
            ctx, currentUser.Object, fidelity.Object, NullLogger<OrderPaymentApplicator>.Instance);
        var assembler = Assembler();
        var handler = new AddTableBillPaymentCommandHandler(
            ctx, applicator, assembler, NullLogger<AddTableBillPaymentCommandHandler>.Instance);

        return await handler.Handle(
            new AddTableBillPaymentCommand
            {
                TableNumber = tableNumber,
                Amount = amount,
                PaymentMethod = PaymentMethod.Cash,
            },
            CancellationToken.None);
    }

    private async Task<Guid> SeedDineInOrderAsync(
        int tableNumber, decimal total, DateTime orderedAt, OrderStatus status = OrderStatus.Confirmed,
        decimal tax = 0m, decimal discount = 0m, decimal tip = 0m)
    {
        var orderId = Guid.NewGuid();
        await using var seed = _fixture.CreateContext();
        seed.Orders.Add(new Order
        {
            Id = orderId,
            OrderNumber = $"TB-{orderId:N}".Substring(0, 12),
            Type = OrderType.DineIn,
            TableNumber = tableNumber,
            Status = status,
            PaymentStatus = PaymentStatus.Pending,
            SubTotal = total,
            Tax = tax,
            Discount = discount,
            Tip = tip,
            Total = total + tax + tip - discount,
            TotalPaid = 0m,
            RemainingAmount = total + tax + tip - discount,
            OrderDate = orderedAt,
            CreatedAt = orderedAt,
            CreatedBy = nameof(TableBillTests),
        });
        await seed.SaveChangesAsync();
        return orderId;
    }

    private async Task SeedTakeawayOrderAsync(decimal total)
    {
        await using var seed = _fixture.CreateContext();
        seed.Orders.Add(new Order
        {
            Id = Guid.NewGuid(),
            OrderNumber = $"TW-{Guid.NewGuid():N}".Substring(0, 12),
            Type = OrderType.Takeaway,
            Status = OrderStatus.Pending,
            PaymentStatus = PaymentStatus.Pending,
            SubTotal = total,
            Total = total,
            RemainingAmount = total,
            OrderDate = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = nameof(TableBillTests),
        });
        await seed.SaveChangesAsync();
    }

    private async Task SeedItemAsync(Guid orderId, string productName, int quantity, decimal unitPrice)
    {
        await using var seed = _fixture.CreateContext();
        seed.OrderItems.Add(new OrderItem
        {
            OrderId = orderId,
            ProductName = productName,
            Quantity = quantity,
            UnitPrice = unitPrice,
            ItemTotal = unitPrice * quantity,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = nameof(TableBillTests),
        });
        await seed.SaveChangesAsync();
    }

    /// <summary>One completed cash tender for an order's full current outstanding — the
    /// per-order till path, used to arrange overpaid / settled rounds.</summary>
    private async Task SettleAsync(Guid orderId, decimal amount)
    {
        await using var seed = _fixture.CreateContext();
        var order = await seed.Orders.Include(o => o.Payments).SingleAsync(o => o.Id == orderId);
        order.Payments.Add(new OrderPayment
        {
            OrderId = orderId,
            PaymentMethod = PaymentMethod.Cash,
            Amount = amount,
            Status = PaymentStatus.Completed,
            PaymentDate = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = nameof(TableBillTests),
        });
        var captured = order.Payments.Where(p => p.Status.IsCaptured()).Sum(p => p.Amount);
        order.TotalPaid = captured;
        order.RemainingAmount = order.Total - captured;
        order.PaymentStatus = order.RemainingAmount > 0.01m
            ? PaymentStatus.PartiallyPaid
            : order.RemainingAmount < -0.01m ? PaymentStatus.Overpaid : PaymentStatus.Completed;
        await seed.SaveChangesAsync();
    }
    [Fact]
    public async Task BillPayment_SkipsAnAlreadySettledRoundAndNeverReachesBack()
    {
        // Round A is fully paid (the overpaid-style case behaves the same: allocation clamps at
        // zero and never moves money backwards). One tender of 15 must land entirely on round B.
        var a = await SeedDineInOrderAsync(tableNumber: 8, total: 30m, orderedAt: Utc(17, 0));
        await SettleAsync(a, amount: 30m);
        var b = await SeedDineInOrderAsync(tableNumber: 8, total: 20m, orderedAt: Utc(17, 30));

        var response = await PayBillAsync(8, amount: 15m);

        response.Success.Should().BeTrue();

        await using var ctx = _fixture.CreateContext();
        var orderA = await ctx.Orders.Include(o => o.Payments).SingleAsync(o => o.Id == a);
        var orderB = await ctx.Orders.Include(o => o.Payments).SingleAsync(o => o.Id == b);

        orderA.Payments.Should().ContainSingle("the settled round keeps exactly its original tender");
        orderA.TotalPaid.Should().Be(30m);
        orderB.TotalPaid.Should().Be(15m, "the whole tender went to the outstanding round");
    }

    [Fact]
    public async Task Bill_SumsTaxDiscountAndTipAcrossOrders()
    {
        await SeedDineInOrderAsync(tableNumber: 6, total: 100m, orderedAt: Utc(12, 0), tax: 8.10m, discount: 10m, tip: 5m);
        await SeedDineInOrderAsync(tableNumber: 6, total: 50m, orderedAt: Utc(12, 30), tax: 4.05m, discount: 0m, tip: 2m);

        var bill = await GetBillAsync(6);

        bill!.Total.Should().Be(159.15m, "Total carries tax + tip − discount, summed over the rounds");
        bill.Tax.Should().Be(12.15m);
        bill.Discount.Should().Be(10m);
        bill.Tip.Should().Be(7m);
    }

    [Fact]
    public async Task BillPayment_MidAllocationFailure_RollsBackEveryTender()
    {
        // THE seam the transaction exists for: the applicator succeeds on the first round and
        // refuses the second (a concurrent change). Money committed for round 1 must NOT stand —
        // the till took nothing, so the ledger may record nothing.
        await SeedDineInOrderAsync(tableNumber: 11, total: 30m, orderedAt: Utc(12, 0));
        await SeedDineInOrderAsync(tableNumber: 11, total: 20m, orderedAt: Utc(12, 45));

        var ctx = _fixture.CreateContext();
        var currentUser = new Mock<ICurrentUserService>();
        var userId = Guid.NewGuid();
        currentUser.Setup(x => x.UserId).Returns(userId);
        currentUser.Setup(x => x.GetAuditIdentifier()).Returns(userId.ToString());

        var calls = 0;
        var applicator = new Mock<IOrderPaymentApplicator>();
        applicator
            .SetupSequence(a => a.ApplyToOrderAsync(It.IsAny<Guid>(), It.IsAny<OrderPaymentTender>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                calls++;
                // Commit a REAL tender for the first round, through the same context, so the
                // assertion below proves the rollback rather than a no-op.
                ctx.OrderPayments.Add(new OrderPayment
                {
                    OrderId = ctx.Orders.OrderBy(o => o.OrderDate).First().Id,
                    PaymentMethod = PaymentMethod.Cash,
                    Amount = 30m,
                    Status = PaymentStatus.Completed,
                    PaymentDate = DateTime.UtcNow,
                    CreatedAt = DateTime.UtcNow,
                    CreatedBy = nameof(TableBillTests),
                });
                ctx.SaveChanges();
                return PaymentApplicationResult.Applied(ctx.Orders.Include(o => o.Payments).OrderBy(o => o.OrderDate).First());
            })
            .ReturnsAsync(PaymentApplicationResult.NotPayable(OrderStatus.Cancelled.ToString()));

        var handler = new AddTableBillPaymentCommandHandler(
            ctx, applicator.Object, Assembler(), NullLogger<AddTableBillPaymentCommandHandler>.Instance);
        var response = await handler.Handle(
            new AddTableBillPaymentCommand { TableNumber = 11, Amount = 40m, PaymentMethod = PaymentMethod.Cash },
            CancellationToken.None);

        response.Success.Should().BeFalse("a half-settled bill must answer as a refusal");
        response.Errors.Should().ContainSingle().Which.Should().Contain("review the bill");

        await using var verify = _fixture.CreateContext();
        verify.OrderPayments.Should().BeEmpty("money recorded before the failure must have been rolled back");
    }

    [Fact]
    public async Task BillPayment_SerializationAbort_MapsToRefusalAndRollsBack()
    {
        // THE concurrency seam: the abort surfaces EF-wrapped (DbUpdateException around
        // PostgresException 40001) from inside the transaction. The refusal must be the
        // friendly mapped one, and nothing may remain in the ledger.
        await SeedDineInOrderAsync(tableNumber: 12, total: 30m, orderedAt: Utc(12, 0));

        var ctx = _fixture.CreateContext();
        var currentUser = new Mock<ICurrentUserService>();
        var userId = Guid.NewGuid();
        currentUser.Setup(x => x.UserId).Returns(userId);
        currentUser.Setup(x => x.GetAuditIdentifier()).Returns(userId.ToString());

        var applicator = new Mock<IOrderPaymentApplicator>();
        applicator
            .Setup(a => a.ApplyToOrderAsync(It.IsAny<Guid>(), It.IsAny<OrderPaymentTender>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DbUpdateException(
                "Concurrent update",
                new PostgresException("could not serialize access", "40P01", "XX40001", "40001")));

        var handler = new AddTableBillPaymentCommandHandler(
            ctx, applicator.Object, Assembler(), NullLogger<AddTableBillPaymentCommandHandler>.Instance);
        var response = await handler.Handle(
            new AddTableBillPaymentCommand { TableNumber = 12, Amount = 10m, PaymentMethod = PaymentMethod.Cash },
            CancellationToken.None);

        response.Success.Should().BeFalse();
        response.Errors.Should().ContainSingle().Which.Should().Contain("review the bill");

        await using var verify = _fixture.CreateContext();
        verify.OrderPayments.Should().BeEmpty("the transaction rolled back with the abort");
    }
}
