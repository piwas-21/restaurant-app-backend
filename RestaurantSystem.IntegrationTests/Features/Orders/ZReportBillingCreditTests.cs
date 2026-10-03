using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.OrderAmendments.Dtos;
using RestaurantSystem.Api.Features.OrderAmendments.Services;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Features.Orders.Queries.GetZReportQuery;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Common;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

[Collection("Database Lane 4")]
public sealed class ZReportBillingCreditTests : IAsyncLifetime
{
    private const string CreatedBy = nameof(ZReportBillingCreditTests);
    private const int TableNumber = 8271;
    private static readonly DateOnly ReportDate = new(2026, 5, 1);
    private static readonly DateTime ReportStart = ReportDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

    private readonly DatabaseFixture _fixture;

    public ZReportBillingCreditTests(DatabaseFixture fixture) =>
        _fixture = fixture ?? throw new ArgumentNullException(nameof(fixture));

    public Task InitializeAsync() => _fixture.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Fully_voided_food_is_removed_from_sales_lines_and_net_sales()
    {
        var pizza = BuildProduct("Voided pizza", ProductType.MainItem);
        var source = BuildOrder("ZR-CREDIT-FULL", 10m, 10m);
        var pizzaLine = Line(source, pizza, quantity: 1, itemTotal: 10m);
        source.Items.Add(pizzaLine);
        var change = BuildVoidChange(pizzaLine, startOrdinal: 1, quantity: 1);
        await SeedCreditedSourceAsync(source, [pizza], [change], creditMinor: 1000);

        var report = await RunReportAsync();

        report.NetSales.Should().Be(0m, "the CHF 10.00 food charge was fully credited");
        report.TotalBillingCredits.Should().Be(10m);
        report.SalesByProductType.Should().BeEmpty();
        report.TopSellingItems.Should().BeEmpty();
    }

    [Fact]
    public async Task Partial_void_keeps_remaining_units_and_reports_tip_separately()
    {
        var pizza = BuildProduct("Three-unit pizza", ProductType.MainItem);
        var source = BuildOrder("ZR-CREDIT-PART", subTotal: 30.01m, total: 31.01m, tip: 1m);
        var pizzaLine = Line(source, pizza, quantity: 3, itemTotal: 30.01m);
        source.Items.Add(pizzaLine);
        var change = BuildVoidChange(pizzaLine, startOrdinal: 1, quantity: 1);
        await SeedCreditedSourceAsync(source, [pizza], [change], creditMinor: 1001);

        var report = await RunReportAsync();

        report.NetSales.Should().Be(20m, "the CHF 10.01 first unit is removed, while CHF 1.00 remains a tip");
        report.TotalTips.Should().Be(1m);
        report.TotalBillingCredits.Should().Be(10.01m);
        report.SalesByProductType.Should().ContainSingle();
        report.SalesByProductType[0].ItemCount.Should().Be(2);
        report.SalesByProductType[0].TotalAmount.Should().Be(20m,
            "product revenue keeps the report's gross item-total meaning, excluding only the removed unit");
        report.TopSellingItems.Should().ContainSingle();
        report.TopSellingItems[0].QuantitySold.Should().Be(2);
        report.TopSellingItems[0].TotalRevenue.Should().Be(20m);
    }

    [Fact]
    public async Task Replacement_omits_original_line_and_counts_linked_supplement_once()
    {
        var original = BuildProduct("Original dish", ProductType.MainItem);
        var replacement = BuildProduct("Replacement dish", ProductType.MainItem);
        var source = BuildOrder("ZR-REPLACE-SOURCE", 10m, 10m);
        var sourceLine = Line(source, original, quantity: 1, itemTotal: 10m);
        source.Items.Add(sourceLine);
        var supplement = BuildOrder("ZR-REPLACE-ADD", 12m, 12m);
        var replacementLine = Line(supplement, replacement, quantity: 1, itemTotal: 12m);
        supplement.Items.Add(replacementLine);
        var change = BuildReplacementChange(sourceLine, replacementLine, supplement, startOrdinal: 1, quantity: 1);
        await SeedCreditedSourceAsync(
            source, [original, replacement], [change], creditMinor: 1000,
            supplement: supplement, addedMinor: 1200);

        var report = await RunReportAsync();

        report.NetSales.Should().Be(12m, "the original CHF 10.00 is credited and the CHF 12.00 supplement is charged once");
        report.TotalBillingCredits.Should().Be(10m);
        report.TotalTransactions.Should().Be(2);
        report.SalesByProductType.Should().ContainSingle();
        report.SalesByProductType[0].ItemCount.Should().Be(1);
        report.SalesByProductType[0].TotalAmount.Should().Be(12m);
        report.TopSellingItems.Should().ContainSingle();
        report.TopSellingItems[0].ProductName.Should().Be("Replacement dish");
        report.TopSellingItems[0].QuantitySold.Should().Be(1);
        report.TopSellingItems[0].TotalRevenue.Should().Be(12m);

        await using var verify = _fixture.CreateContext();
        var persistedSource = await verify.Orders.Include(order => order.Items)
            .SingleAsync(order => order.Id == source.Id);
        persistedSource.Total.Should().Be(10m);
        persistedSource.BillingCreditAmount.Should().Be(10m);
        persistedSource.Items.Should().ContainSingle();
        persistedSource.Items.Single().Id.Should().Be(sourceLine.Id);
        persistedSource.Items.Single().ProductName.Should().Be("Original dish");
        persistedSource.Items.Single().Quantity.Should().Be(1);
        persistedSource.Items.Single().ItemTotal.Should().Be(10m);
    }

    [Fact]
    public async Task Retained_zero_price_units_still_appear_in_product_and_top_item_counts()
    {
        var pizza = BuildProduct("Voided paid dish", ProductType.MainItem);
        var side = BuildProduct("Complimentary side", ProductType.Beverage);
        var source = BuildOrder("ZR-CREDIT-FREE-UNIT", 10m, 10m);
        var pizzaLine = Line(source, pizza, quantity: 1, itemTotal: 10m);
        var freeLine = Line(source, side, quantity: 2, itemTotal: 0m);
        source.Items.Add(pizzaLine);
        source.Items.Add(freeLine);
        await SeedCreditedSourceAsync(
            source, [pizza, side], [BuildVoidChange(pizzaLine, startOrdinal: 1, quantity: 1)], creditMinor: 1000);

        var report = await RunReportAsync();

        report.NetSales.Should().Be(0m);
        report.TotalBillingCredits.Should().Be(10m);
        report.SalesByProductType.Should().ContainSingle();
        report.SalesByProductType[0].ProductType.Should().Be(nameof(ProductType.Beverage));
        report.SalesByProductType[0].ItemCount.Should().Be(2);
        report.SalesByProductType[0].TotalAmount.Should().Be(0m);
        report.TopSellingItems.Should().ContainSingle();
        report.TopSellingItems[0].ProductName.Should().Be("Complimentary side");
        report.TopSellingItems[0].QuantitySold.Should().Be(2);
        report.TopSellingItems[0].TotalRevenue.Should().Be(0m);
    }

    [Fact]
    public async Task Resolved_credit_without_immutable_journal_fails_before_report_is_returned()
    {
        var pizza = BuildProduct("Unjournaled pizza", ProductType.MainItem);
        var source = BuildOrder("ZR-NO-JOURNAL", 10m, 10m);
        var pizzaLine = Line(source, pizza, quantity: 1, itemTotal: 10m);
        source.Items.Add(pizzaLine);
        await SeedCreditedSourceAsync(
            source, [pizza], [BuildVoidChange(pizzaLine, startOrdinal: 1, quantity: 1)],
            creditMinor: 1000, writeJournal: false);

        var error = await Record.ExceptionAsync(async () => await RunReportAsync());

        error.Should().BeOfType<ConflictException>();
    }

    [Fact]
    public async Task Free_dish_void_is_removed_without_credit_and_retained_units_stay_unchanged()
    {
        var freeDish = BuildProduct("Complimentary pastry", ProductType.MainItem);
        var retainedDish = BuildProduct("Retained soup", ProductType.Beverage);
        var source = BuildOrder("ZR-ZERO-CREDIT-FREE", 6m, 6m);
        var freeLine = Line(source, freeDish, quantity: 1, itemTotal: 0m);
        var retainedLine = Line(source, retainedDish, quantity: 2, itemTotal: 6m);
        source.Items.Add(freeLine);
        source.Items.Add(retainedLine);
        await SeedCreditedSourceAsync(
            source, [freeDish, retainedDish], [BuildVoidChange(freeLine, startOrdinal: 1, quantity: 1)],
            creditMinor: 0);

        var report = await RunReportAsync();

        report.NetSales.Should().Be(6m);
        report.TotalBillingCredits.Should().Be(0m);
        report.SalesByProductType.Should().ContainSingle();
        report.SalesByProductType[0].ProductType.Should().Be(nameof(ProductType.Beverage));
        report.SalesByProductType[0].ItemCount.Should().Be(2);
        report.SalesByProductType[0].TotalAmount.Should().Be(6m);
        report.TopSellingItems.Should().ContainSingle();
        report.TopSellingItems[0].ProductName.Should().Be("Retained soup");
        report.TopSellingItems[0].QuantitySold.Should().Be(2);
        report.TopSellingItems[0].TotalRevenue.Should().Be(6m);

        await using var verify = _fixture.CreateContext();
        (await verify.OrderBillingCredits.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Zero_minor_unit_void_reduces_quantity_without_reducing_revenue()
    {
        var tinyDish = BuildProduct("One-cent dish", ProductType.MainItem);
        var source = BuildOrder("ZR-ZERO-MINOR-UNIT", 0.01m, 0.01m);
        var line = Line(source, tinyDish, quantity: 3, itemTotal: 0.01m);
        source.Items.Add(line);
        await SeedCreditedSourceAsync(
            source, [tinyDish], [BuildVoidChange(line, startOrdinal: 2, quantity: 1)], creditMinor: 0);

        var report = await RunReportAsync();

        report.NetSales.Should().Be(0.01m);
        report.TotalBillingCredits.Should().Be(0m);
        report.SalesByProductType.Should().ContainSingle();
        report.SalesByProductType[0].ItemCount.Should().Be(2);
        report.SalesByProductType[0].TotalAmount.Should().Be(0.01m);
        report.TopSellingItems.Should().ContainSingle();
        report.TopSellingItems[0].QuantitySold.Should().Be(2);
        report.TopSellingItems[0].TotalRevenue.Should().Be(0.01m);

        await using var verify = _fixture.CreateContext();
        (await verify.OrderBillingCredits.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Zero_value_replacement_omits_original_and_counts_linked_supplement_once()
    {
        var freeDish = BuildProduct("Original complimentary dish", ProductType.MainItem);
        var replacementDish = BuildProduct("Replacement soup", ProductType.Beverage);
        var source = BuildOrder("ZR-ZERO-SOURCE", 0m, 0m);
        var originalLine = Line(source, freeDish, quantity: 1, itemTotal: 0m);
        source.Items.Add(originalLine);
        var supplement = BuildOrder("ZR-ZERO-ADD", 5m, 5m);
        var replacementLine = Line(supplement, replacementDish, quantity: 1, itemTotal: 5m);
        supplement.Items.Add(replacementLine);
        var change = BuildReplacementChange(
            originalLine, replacementLine, supplement, startOrdinal: 1, quantity: 1);
        await SeedCreditedSourceAsync(
            source, [freeDish, replacementDish], [change], creditMinor: 0,
            supplement: supplement, addedMinor: 500);

        var report = await RunReportAsync();

        report.NetSales.Should().Be(5m);
        report.TotalBillingCredits.Should().Be(0m);
        report.SalesByProductType.Should().ContainSingle();
        report.SalesByProductType[0].ProductType.Should().Be(nameof(ProductType.Beverage));
        report.SalesByProductType[0].ItemCount.Should().Be(1);
        report.SalesByProductType[0].TotalAmount.Should().Be(5m);
        report.TopSellingItems.Should().ContainSingle();
        report.TopSellingItems[0].ProductName.Should().Be("Replacement soup");
        report.TopSellingItems[0].QuantitySold.Should().Be(1);
        report.TopSellingItems[0].TotalRevenue.Should().Be(5m);

        await using var verify = _fixture.CreateContext();
        (await verify.OrderBillingCredits.CountAsync()).Should().Be(0);
    }

    private async Task SeedCreditedSourceAsync(
        Order source,
        IReadOnlyList<Product> products,
        IReadOnlyList<OrderAmendmentChangeSnapshot> changes,
        long creditMinor,
        Order? supplement = null,
        long addedMinor = 0,
        bool writeJournal = true)
    {
        var now = DateTime.UtcNow;
        var sessionId = Guid.NewGuid();
        var session = new TableServiceSession
        {
            Id = sessionId,
            TableNumber = TableNumber,
            Currency = "CHF",
            Status = TableServiceSessionStatus.Open,
            Version = 2,
            AccountRevision = 2,
            BillingAllocationVersion = 1,
            OpenedAt = ReportStart.AddHours(-1),
            CreatedAt = now,
            CreatedBy = CreatedBy
        };
        source.ServiceSessionId = sessionId;
        source.TableNumber = TableNumber;
        source.BillingCreditAmount = creditMinor / 100m;
        source.RemainingAmount = source.Total - source.BillingCreditAmount;
        if (supplement is not null)
        {
            supplement.ServiceSessionId = sessionId;
            supplement.TableNumber = TableNumber;
        }

        var amendmentId = Guid.NewGuid();
        var hasCredit = creditMinor > 0;
        var outcome = new OrderAmendmentFinancialPreviewDto(
            "CHF", addedMinor, creditMinor, addedMinor - creditMinor, creditMinor,
            hasCredit ? OrderAmendmentFinancialResolutionStatus.Resolved
                : OrderAmendmentFinancialResolutionStatus.NotRequired,
            hasCredit ? OrderAmendmentCreditState.BalanceReduction : OrderAmendmentCreditState.None,
            OrderAmendmentLoyaltyState.None,
            OrderAmendmentRefundState.None);
        var amendment = new OrderAmendment
        {
            Id = amendmentId,
            SourceOrderId = source.Id,
            ServiceSessionId = sessionId,
            SupplementOrderId = supplement?.Id,
            ClientOperationId = Guid.NewGuid(),
            ActorUserId = Guid.Parse("a0000000-0000-0000-0000-000000000003"),
            ActorRole = "Cashier",
            State = OrderAmendmentState.Committed,
            PayloadHash = new string('a', 64),
            CommitPayloadHash = new string('b', 64),
            ExpectedOrderVersion = source.Version,
            ExpectedAccountRevision = 1,
            CommittedAccountRevision = 2,
            ExpiresAt = now.AddMinutes(10),
            CommittedAt = now,
            RequestJson = "{}",
            ChangesJson = OrderAmendmentJson.Serialize(changes),
            SourceSnapshotJson = OrderAmendmentJson.Serialize(new OrderAmendmentSourceSnapshot(
                source.Id, source.OrderNumber, source.Type, source.Status, source.IsKitchenReleased,
                sessionId, source.Version, session.Currency, source.Total,
                source.Items.Select(ToSnapshot).ToArray())),
            SupplementSnapshotJson = supplement is null ? null : OrderAmendmentJson.Serialize(
                CreateSupplementSnapshot(supplement, sessionId, session.Currency)),
            FinancialResolutionJson = OrderAmendmentJson.Serialize(outcome),
            CommitResultJson = "{}",
            CreatedAt = now,
            CreatedBy = CreatedBy
        };

        await using var context = _fixture.CreateContext();
        context.TableServiceSessions.Add(session);
        context.Products.AddRange(products);
        context.Orders.Add(source);
        if (supplement is not null)
            context.Orders.Add(supplement);
        context.Set<OrderAmendment>().Add(amendment);
        if (writeJournal && creditMinor > 0)
        {
            context.OrderBillingCredits.Add(new OrderBillingCredit
            {
                Id = Guid.NewGuid(),
                SourceOrderId = source.Id,
                AmendmentId = amendment.Id,
                AmountMinor = creditMinor,
                Currency = "CHF",
                ActorUserId = amendment.ActorUserId,
                ActorRole = amendment.ActorRole,
                CreatedAt = now,
                CreatedBy = CreatedBy
            });
        }
        await context.SaveChangesAsync();
    }

    private async Task<ZReportDto> RunReportAsync()
    {
        await using var context = _fixture.CreateContext();
        var handler = new GetZReportQueryHandler(
            context, new FixedTenantClock("UTC"), NullLogger<GetZReportQueryHandler>.Instance);
        var response = await handler.Handle(new GetZReportQuery(ReportDate), CancellationToken.None);
        response.Success.Should().BeTrue();
        response.Data.Should().NotBeNull();
        return response.Data!;
    }

    private static Order BuildOrder(string number, decimal subTotal, decimal total, decimal tip = 0m)
    {
        var now = DateTime.UtcNow;
        return new Order
        {
            Id = Guid.NewGuid(),
            OrderNumber = number,
            Type = OrderType.DineIn,
            TableNumber = TableNumber,
            Status = OrderStatus.Completed,
            PaymentStatus = PaymentStatus.Pending,
            SubTotal = subTotal,
            Tip = tip,
            Total = total,
            TotalPaid = 0m,
            RemainingAmount = total,
            OrderDate = ReportStart.AddHours(12),
            CreatedAt = now,
            CreatedBy = CreatedBy
        };
    }

    private static Product BuildProduct(string name, ProductType type) => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        BasePrice = 1m,
        IsActive = true,
        IsAvailable = true,
        Type = type,
        PreparationTimeMinutes = 0,
        CreatedAt = DateTime.UtcNow,
        CreatedBy = CreatedBy
    };

    private static OrderItem Line(Order order, Product product, int quantity, decimal itemTotal) => new()
    {
        Id = Guid.NewGuid(),
        OrderId = order.Id,
        ProductId = product.Id,
        Product = product,
        ProductName = product.Name,
        Quantity = quantity,
        UnitPrice = quantity == 0 ? 0m : decimal.Round(itemTotal / quantity, 2),
        ItemTotal = itemTotal,
        CreatedAt = DateTime.UtcNow,
        CreatedBy = CreatedBy
    };

    private static OrderAmendmentChangeSnapshot BuildVoidChange(OrderItem item, int startOrdinal, int quantity) =>
        new(item.Id, OrderAmendmentChangeKind.Void, startOrdinal, quantity, false,
            ToSnapshot(item) with { Quantity = quantity }, null);

    private static OrderAmendmentChangeSnapshot BuildReplacementChange(
        OrderItem original, OrderItem replacement, Order supplement, int startOrdinal, int quantity) =>
        new(original.Id, OrderAmendmentChangeKind.Replace, startOrdinal, quantity, false,
            ToSnapshot(original) with { Quantity = quantity }, ToSnapshot(replacement), supplement.Id,
            supplement.OrderNumber);

    private static OrderItemDto ToSnapshot(OrderItem item) => new()
    {
        Id = item.Id,
        ProductId = item.ProductId,
        ProductVariationId = item.ProductVariationId,
        MenuID = item.MenuId,
        ProductName = item.ProductName,
        VariationName = item.VariationName,
        Quantity = item.Quantity,
        UnitPrice = item.UnitPrice,
        ItemTotal = item.ItemTotal,
        SpecialInstructions = item.SpecialInstructions,
        KitchenType = item.Product?.KitchenType.ToString(),
        Kind = item.Kind
    };

    private static OrderAmendmentSupplementSnapshot CreateSupplementSnapshot(
        Order supplement, Guid sessionId, string currency)
    {
        var items = supplement.Items.Select(ToSnapshot).ToList();
        var dto = new OrderDto
        {
            Id = supplement.Id,
            OrderNumber = supplement.OrderNumber,
            Type = supplement.Type.ToString(),
            ServiceSessionId = sessionId,
            Currency = currency,
            SubTotal = supplement.SubTotal,
            Tax = supplement.Tax,
            DeliveryFee = supplement.DeliveryFee,
            Discount = supplement.Discount,
            DiscountPercentage = supplement.DiscountPercentage,
            CustomerDiscountAmount = supplement.CustomerDiscountAmount,
            FidelityPointsDiscount = supplement.FidelityPointsDiscount,
            FidelityPointsEarned = supplement.FidelityPointsEarned,
            FidelityPointsRedeemed = supplement.FidelityPointsRedeemed,
            Tip = supplement.Tip,
            Total = supplement.Total,
            Items = items
        };
        return new OrderAmendmentSupplementSnapshot(
            supplement.Id, supplement.OrderNumber, supplement.Type, supplement.IsKitchenReleased,
            sessionId, currency, supplement.Total, OrderAmendmentJson.PricingFingerprint(dto), items);
    }
}
