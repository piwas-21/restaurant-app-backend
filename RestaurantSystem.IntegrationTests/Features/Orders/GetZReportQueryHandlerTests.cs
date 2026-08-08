using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using RestaurantSystem.Api.Features.Orders.Queries.GetZReportQuery;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

/// <summary>
/// Handler-focused integration tests for <see cref="GetZReportQueryHandler"/>
/// that drive the handler directly against a real <see cref="ApplicationDbContext"/>
/// backed by the shared <see cref="DatabaseFixture"/>. We deliberately bypass
/// the HTTP layer because the surface under test is the in-memory aggregation
/// pipeline (cancelled-order exclusion, root-item filtering, top-N cap,
/// decimal precision). Driving via HTTP would only add noise.
///
/// Each test resets the DB via <see cref="DatabaseFixture.ResetDatabaseAsync"/>
/// so order state from earlier cases doesn't leak. All <see cref="DateTime"/>
/// values are <see cref="DateTimeKind.Utc"/> to match the handler's contract
/// (UTC day boundaries).
/// </summary>
[Collection("Database")]
public class GetZReportQueryHandlerTests : IAsyncLifetime
{
    private readonly DatabaseFixture _fixture;

    private static readonly DateOnly ReportDate = new(2026, 5, 1);
    private static readonly DateTime StartOfDay =
        ReportDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

    public GetZReportQueryHandlerTests(DatabaseFixture fixture)
    {
        _fixture = fixture ?? throw new ArgumentNullException(nameof(fixture));
    }

    public Task InitializeAsync() => _fixture.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    // ---------------------------------------------------------------------
    // Tests
    // ---------------------------------------------------------------------

    [Fact]
    public async Task CancelledOrders_ExcludedFromSalesIncludedInCancellations()
    {
        await using (var seed = _fixture.CreateContext())
        {
            // One confirmed sale, one cancelled — both same day.
            seed.Orders.Add(BuildOrder(
                "ZR-CONF",
                StartOfDay.AddHours(10),
                OrderStatus.Confirmed,
                subTotal: 100m,
                total: 110m,
                discount: 5m,
                payments: new[] { BuildPayment(PaymentMethod.Cash, 110m, PaymentStatus.Completed) }));

            seed.Orders.Add(BuildOrder(
                "ZR-CANC",
                StartOfDay.AddHours(11),
                OrderStatus.Cancelled,
                subTotal: 200m,
                total: 220m,
                discount: 50m,
                payments: new[] { BuildPayment(PaymentMethod.CreditCard, 220m, PaymentStatus.Completed) }));

            await seed.SaveChangesAsync();
        }

        var report = await RunHandlerAsync();

        report.TotalTransactions.Should().Be(1, "cancelled orders are excluded from sales counts");
        report.GrossSales.Should().Be(100m);
        report.NetSales.Should().Be(110m);
        report.Discounts.TotalDiscounts.Should().Be(5m, "cancelled discount must NOT bleed into discount totals");
        report.CancelledOrdersCount.Should().Be(1);
        report.CancelledOrdersTotal.Should().Be(220m);
        report.PaymentsByMethod.Should().ContainSingle()
            .Which.PaymentMethod.Should().Be(nameof(PaymentMethod.Cash),
                "the cancelled order's CreditCard payment must not appear in PaymentsByMethod");
    }

    [Fact]
    public async Task RefundedPayments_CountedInRefundsAndStillInPaymentsByMethod()
    {
        // Documents the intentional relationship: a refunded payment surfaces in
        // BOTH PaymentsByMethod (as the original gross transaction) AND in the
        // Refunds aggregate (via RefundedAmount). They are not double-counts —
        // they answer different questions ("what was charged" vs "what was given
        // back"). Pinning that here so a future "fix" doesn't silently drop one.
        await using (var seed = _fixture.CreateContext())
        {
            seed.Orders.Add(BuildOrder(
                "ZR-REF",
                StartOfDay.AddHours(12),
                OrderStatus.Completed,
                subTotal: 50m,
                total: 50m,
                payments: new[]
                {
                    BuildFullyRefundedPayment(PaymentMethod.CreditCard, 50m),
                }));
            await seed.SaveChangesAsync();
        }

        var report = await RunHandlerAsync();

        // The refunded order is non-cancelled, so it still counts as a sale.
        report.TotalTransactions.Should().Be(1);

        report.Refunds.RefundCount.Should().Be(1);
        report.Refunds.TotalRefundedAmount.Should().Be(50m);

        report.PaymentsByMethod.Should().ContainSingle();
        var card = report.PaymentsByMethod[0];
        card.PaymentMethod.Should().Be(nameof(PaymentMethod.CreditCard));
        card.TransactionCount.Should().Be(1);
        card.TotalAmount.Should().Be(50m,
            "PaymentsByMethod uses Payment.Amount (gross), Refunds uses RefundedAmount — same payment, different facets");
    }

    [Fact]
    public async Task PartiallyRefundedPayment_AppearsInBothPaymentsByMethodAndRefunds()
    {
        // Issue #286, READER half only. Seeding the status the corrected writer
        // stores, the Refunds assertion below is the one that fails without the
        // fix — it used to filter on IsRefunded, which is set only for a FULL
        // refund, so the CHF 20 that left the till was never counted.
        //
        // The PaymentsByMethod assertion does NOT fail without the fix and is
        // not claimed to: the old filter listed PartiallyRefunded explicitly,
        // so it is the same set IsCaptured() names. That half of the defect
        // lived entirely in the WRITER, which stored PartiallyPaid instead —
        // see RefundPaymentCommandHandlerTests for the seam test that pins the
        // two ends agreeing.
        await using (var seed = _fixture.CreateContext())
        {
            seed.Orders.Add(BuildOrder(
                "ZR-PARTREF",
                StartOfDay.AddHours(12),
                OrderStatus.Completed,
                subTotal: 50m,
                total: 50m,
                payments: new[]
                {
                    BuildPartiallyRefundedPayment(PaymentMethod.CreditCard, 50m, refundedAmount: 20m),
                }));
            await seed.SaveChangesAsync();
        }

        var report = await RunHandlerAsync();

        report.Refunds.RefundCount.Should().Be(1);
        report.Refunds.TotalRefundedAmount.Should().Be(20m);

        report.PaymentsByMethod.Should().ContainSingle();
        report.PaymentsByMethod[0].PaymentMethod.Should().Be(nameof(PaymentMethod.CreditCard));
        report.PaymentsByMethod[0].TotalAmount.Should().Be(50m, "the gross charge is unchanged by a refund");
    }

    [Fact]
    public async Task PendingPayment_ExcludedFromPaymentsByMethod()
    {
        // The other side of IsCaptured: cash stays Pending until a cashier
        // completes it, and money not yet taken must not appear in the day's
        // takings. Pinned because the captured set is now shared with the
        // order's TotalPaid arithmetic, where the same mistake would overstate
        // what the till holds.
        await using (var seed = _fixture.CreateContext())
        {
            seed.Orders.Add(BuildOrder(
                "ZR-PEND",
                StartOfDay.AddHours(9),
                OrderStatus.Confirmed,
                subTotal: 30m,
                total: 30m,
                payments: new[] { BuildPayment(PaymentMethod.Cash, 30m, PaymentStatus.Pending) }));
            await seed.SaveChangesAsync();
        }

        var report = await RunHandlerAsync();

        report.TotalTransactions.Should().Be(1, "the order is still a sale");
        report.PaymentsByMethod.Should().BeEmpty("a Pending tender has not been taken");
    }

    [Fact]
    public async Task ChildBundleItems_FilteredOutOfProductTypeAndTopItems()
    {
        Guid mainProductId;
        Guid sauceProductId;
        await using (var seed = _fixture.CreateContext())
        {
            var main = BuildProduct("Bundle Pizza", ProductType.MainItem);
            var sauce = BuildProduct("Bundled Sauce", ProductType.Sauce);
            seed.Products.AddRange(main, sauce);
            mainProductId = main.Id;
            sauceProductId = sauce.Id;

            var order = BuildOrder("ZR-BUN", StartOfDay.AddHours(13), OrderStatus.Completed,
                subTotal: 25m, total: 25m);

            var parent = new OrderItem
            {
                Id = Guid.NewGuid(),
                OrderId = order.Id,
                ProductId = mainProductId,
                ParentOrderItemId = null,
                ProductName = "Bundle Pizza",
                Quantity = 1,
                UnitPrice = 25m,
                ItemTotal = 25m,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = "GetZReportQueryHandlerTests",
            };
            var child = new OrderItem
            {
                Id = Guid.NewGuid(),
                OrderId = order.Id,
                ProductId = sauceProductId,
                ParentOrderItemId = parent.Id,
                ProductName = "Bundled Sauce",
                Quantity = 1,
                UnitPrice = 0m,
                ItemTotal = 0m,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = "GetZReportQueryHandlerTests",
            };
            order.Items.Add(parent);
            order.Items.Add(child);

            seed.Orders.Add(order);
            await seed.SaveChangesAsync();
        }

        var report = await RunHandlerAsync();

        // Order-level totals must reflect the parent's total only — child has
        // ItemTotal = 0 in this seed but more importantly, top-level
        // GrossSales/NetSales come from Order.SubTotal/Total, not from
        // summed items, so they're unaffected by item filtering.
        report.TotalTransactions.Should().Be(1);
        report.GrossSales.Should().Be(25m);
        report.NetSales.Should().Be(25m);

        report.SalesByProductType.Should().ContainSingle(
            "the child bundle item must not produce a Sauce row");
        report.SalesByProductType[0].ProductType.Should().Be(nameof(ProductType.MainItem));

        report.TopSellingItems.Should().ContainSingle();
        report.TopSellingItems[0].ProductName.Should().Be("Bundle Pizza");
    }

    [Fact]
    public async Task EmptyDay_ReturnsZeroedReportNotNull()
    {
        // No seed — DB was just reset.
        var report = await RunHandlerAsync();

        report.Should().NotBeNull();
        report.TotalTransactions.Should().Be(0);
        report.GrossSales.Should().Be(0m);
        report.NetSales.Should().Be(0m);
        report.TotalTax.Should().Be(0m);
        report.TotalTips.Should().Be(0m);
        report.TotalDeliveryFees.Should().Be(0m);
        report.CancelledOrdersCount.Should().Be(0);
        report.CancelledOrdersTotal.Should().Be(0m);
        report.Discounts.TotalDiscounts.Should().Be(0m);
        report.Refunds.RefundCount.Should().Be(0);
        report.Refunds.TotalRefundedAmount.Should().Be(0m);
        report.PaymentsByMethod.Should().NotBeNull().And.BeEmpty();
        report.SalesByOrderType.Should().NotBeNull().And.BeEmpty();
        report.SalesByProductType.Should().NotBeNull().And.BeEmpty();
        report.TopSellingItems.Should().NotBeNull().And.BeEmpty();
        report.ReportDate.Should().Be(StartOfDay);
        report.ReportDate.Kind.Should().Be(DateTimeKind.Utc);
    }

    [Fact]
    public async Task DateWindow_IncludesStartTickExcludesPriorTickAndNextDay()
    {
        await using (var seed = _fixture.CreateContext())
        {
            // OrderDate = startOfDay − 1 tick → MUST be excluded.
            seed.Orders.Add(BuildOrder("ZR-PRE", StartOfDay.AddTicks(-1),
                OrderStatus.Confirmed, subTotal: 10m, total: 10m));

            // OrderDate = startOfDay → MUST be included.
            seed.Orders.Add(BuildOrder("ZR-START", StartOfDay,
                OrderStatus.Confirmed, subTotal: 20m, total: 20m));

            // OrderDate = startOfNextDay → MUST be excluded (half-open upper).
            seed.Orders.Add(BuildOrder("ZR-NEXT", StartOfDay.AddDays(1),
                OrderStatus.Confirmed, subTotal: 40m, total: 40m));

            await seed.SaveChangesAsync();
        }

        var report = await RunHandlerAsync();

        report.TotalTransactions.Should().Be(1);
        report.GrossSales.Should().Be(20m, "only ZR-START is inside the [start, nextDay) window");
        report.NetSales.Should().Be(20m);
    }

    [Fact]
    public async Task TopSellingItems_CappedAtTen()
    {
        await using (var seed = _fixture.CreateContext())
        {
            // 15 distinct products, each in its own order, with descending
            // quantities so ordering is deterministic. The handler must return
            // exactly the top 10 (TopItemsCount const).
            for (int i = 0; i < 15; i++)
            {
                var quantity = 100 - i; // 100, 99, ..., 86 — strictly descending
                var product = BuildProduct($"Top-{i:D2}", ProductType.MainItem);
                seed.Products.Add(product);

                var order = BuildOrder(
                    $"ZR-TOP-{i:D2}",
                    StartOfDay.AddMinutes(i),
                    OrderStatus.Completed,
                    subTotal: quantity * 1m,
                    total: quantity * 1m);

                order.Items.Add(new OrderItem
                {
                    Id = Guid.NewGuid(),
                    OrderId = order.Id,
                    ProductId = product.Id,
                    ParentOrderItemId = null,
                    ProductName = product.Name,
                    Quantity = quantity,
                    UnitPrice = 1m,
                    ItemTotal = quantity * 1m,
                    CreatedAt = DateTime.UtcNow,
                    CreatedBy = "GetZReportQueryHandlerTests",
                });
                seed.Orders.Add(order);
            }
            await seed.SaveChangesAsync();
        }

        var report = await RunHandlerAsync();

        // All 15 orders processed; the cap applies to TopSellingItems only.
        report.TotalTransactions.Should().Be(15);

        report.TopSellingItems.Should().HaveCount(10, "TopItemsCount const = 10");
        report.TopSellingItems.Select(t => t.ProductName)
            .Should().BeEquivalentTo(
                Enumerable.Range(0, 10).Select(i => $"Top-{i:D2}"),
                "the 10 highest-quantity products are Top-00 through Top-09");
        report.TopSellingItems[0].QuantitySold.Should().Be(100);
        report.TopSellingItems[9].QuantitySold.Should().Be(91);
    }

    [Fact]
    public async Task DecimalPrecision_PreservedAcrossSums()
    {
        // CHF 19.99 × 7 = 139.93 exact. Any double drift would surface as
        // 139.92999999... — fail-loud here so a refactor that introduces
        // floating-point math (e.g. .Average()) gets caught.
        const decimal unit = 19.99m;
        const int count = 7;
        const decimal expected = 139.93m;

        await using (var seed = _fixture.CreateContext())
        {
            for (int i = 0; i < count; i++)
            {
                seed.Orders.Add(BuildOrder(
                    $"ZR-PR-{i:D2}",
                    StartOfDay.AddMinutes(i),
                    OrderStatus.Completed,
                    subTotal: unit,
                    total: unit,
                    payments: new[] { BuildPayment(PaymentMethod.Cash, unit, PaymentStatus.Completed) }));
            }
            await seed.SaveChangesAsync();
        }

        var report = await RunHandlerAsync();

        // All 7 orders processed; precision check is meaningful only if every
        // record is summed.
        report.TotalTransactions.Should().Be(count);

        report.GrossSales.Should().Be(expected);
        report.NetSales.Should().Be(expected);
        report.PaymentsByMethod.Should().ContainSingle()
            .Which.TotalAmount.Should().Be(expected);
    }

    /// <summary>
    /// Net sales is revenue, and a tip is not revenue — it is money in transit to the staff. Since
    /// S0b <c>order.Total</c> is what the customer was charged and therefore reliably includes the
    /// tip, so summing <c>Total</c> unadjusted would overstate the day's turnover by every franc
    /// tipped. That is also the direction Swiss VAT cares about: a voluntary tip only stays outside
    /// taxable consideration while it is disclosed separately and carries no tax
    /// (ESTV MWST-Branchen-Info 08 §8.3), which <c>TotalTips</c> is here to do.
    ///
    /// <para>
    /// The two figures are asserted together deliberately. Subtracting the tip from net sales while
    /// also dropping it from <c>TotalTips</c> would make the money vanish from the report entirely,
    /// and an assertion on either line alone would not notice.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Tips_are_reported_beside_net_sales_and_not_inside_it()
    {
        await using (var seed = _fixture.CreateContext())
        {
            // Charged 46.00: 40.00 of food and a 6.00 tip.
            seed.Orders.Add(BuildOrder("Z-TIP-1", StartOfDay.AddHours(9), OrderStatus.Completed,
                subTotal: 40.00m, total: 46.00m, tip: 6.00m));
            seed.Orders.Add(BuildOrder("Z-TIP-2", StartOfDay.AddHours(10), OrderStatus.Completed,
                subTotal: 20.00m, total: 20.00m));
            await seed.SaveChangesAsync();
        }

        var report = await RunHandlerAsync();

        report.NetSales.Should().Be(60.00m, "the 6.00 tip is not the restaurant's sales revenue");
        report.TotalTips.Should().Be(6.00m, "it is disclosed on its own line, not discarded");
    }

    // ---------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------

    private async Task<RestaurantSystem.Api.Features.Orders.Dtos.ZReportDto> RunHandlerAsync()
    {
        await using var ctx = _fixture.CreateContext();
        var handler = new GetZReportQueryHandler(
            ctx,
            NullLogger<GetZReportQueryHandler>.Instance);

        var response = await handler.Handle(new GetZReportQuery(ReportDate), CancellationToken.None);

        response.Should().NotBeNull();
        response.Success.Should().BeTrue();
        response.Data.Should().NotBeNull();
        return response.Data!;
    }

    private static Order BuildOrder(
        string orderNumber,
        DateTime orderDateUtc,
        OrderStatus status,
        decimal subTotal = 0m,
        decimal total = 0m,
        decimal discount = 0m,
        decimal tip = 0m,
        IEnumerable<OrderPayment>? payments = null)
    {
        var orderId = Guid.NewGuid();
        var order = new Order
        {
            Id = orderId,
            OrderNumber = orderNumber,
            Type = OrderType.Takeaway,
            Status = status,
            PaymentStatus = PaymentStatus.Completed,
            SubTotal = subTotal,
            Total = total,
            Discount = discount,
            Tip = tip,
            OrderDate = DateTime.SpecifyKind(orderDateUtc, DateTimeKind.Utc),
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "GetZReportQueryHandlerTests",
            IsDeleted = false,
        };

        if (payments != null)
        {
            foreach (var p in payments)
            {
                p.OrderId = orderId;
                order.Payments.Add(p);
            }
        }

        return order;
    }

    private static OrderPayment BuildPayment(PaymentMethod method, decimal amount, PaymentStatus status)
        => new()
        {
            Id = Guid.NewGuid(),
            PaymentMethod = method,
            Amount = amount,
            Status = status,
            PaymentDate = DateTime.UtcNow,
            IsRefunded = false,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "GetZReportQueryHandlerTests",
        };

    /// <summary>
    /// A payment refunded in full — the shape <c>RefundPaymentCommand</c>
    /// writes when <c>RefundAmount == Amount</c>: <c>IsRefunded</c> set,
    /// <c>RefundedAmount</c> equal to the charge, status <c>Refunded</c>.
    /// </summary>
    private static OrderPayment BuildFullyRefundedPayment(PaymentMethod method, decimal amount)
        => new()
        {
            Id = Guid.NewGuid(),
            PaymentMethod = method,
            Amount = amount,
            Status = PaymentStatus.Refunded,
            PaymentDate = DateTime.UtcNow,
            IsRefunded = true,
            RefundedAmount = amount,
            RefundDate = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "GetZReportQueryHandlerTests",
        };

    /// <summary>
    /// A payment refunded in part. <c>IsRefunded</c> stays <b>false</b> — the
    /// writer only sets it when the whole charge goes back — which is exactly
    /// why the Refunds aggregate keys on <c>RefundedAmount</c> and not on that
    /// flag. Note this fixture is reachable: it is byte-for-byte what
    /// <c>RefundPaymentCommand</c> now persists for a partial refund.
    /// </summary>
    private static OrderPayment BuildPartiallyRefundedPayment(PaymentMethod method, decimal amount, decimal refundedAmount)
        => new()
        {
            Id = Guid.NewGuid(),
            PaymentMethod = method,
            Amount = amount,
            Status = PaymentStatus.PartiallyRefunded,
            PaymentDate = DateTime.UtcNow,
            IsRefunded = false,
            RefundedAmount = refundedAmount,
            RefundDate = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "GetZReportQueryHandlerTests",
        };

    private static Product BuildProduct(string name, ProductType type)
        => new()
        {
            Id = Guid.NewGuid(),
            Name = name,
            BasePrice = 1m,
            IsActive = true,
            IsAvailable = true,
            PreparationTimeMinutes = 0,
            Type = type,
            DisplayOrder = 0,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "GetZReportQueryHandlerTests",
        };
}
