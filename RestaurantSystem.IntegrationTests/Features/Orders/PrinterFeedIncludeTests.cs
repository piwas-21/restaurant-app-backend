using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using RestaurantSystem.Api.Common;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Infrastructure;
using FeedQuery = RestaurantSystem.Api.Features.Orders.Queries.PrinterFeedQuery.PrinterFeedQuery;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

/// <summary>
/// Issue #234 — missing-include class (same family as #231 / #233): the printer feed loaded a
/// narrow entity graph while <c>OrderMappingService</c> read the absent navigations through
/// null-conditionals, so nothing threw and DTO fields silently came back null/empty.
/// <para>
/// The feed is <c>AsNoTracking</c>, so unlike the tracked query paths there is no EF
/// relationship fix-up to accidentally compensate. These tests drive the real handler through
/// the mediator so the query's includes — not just the mapper — are under test; asserting
/// against the mapper alone would pass with the includes still missing.
/// </para>
/// </summary>
public class PrinterFeedIncludeTests : IntegrationTestBase
{
    public PrinterFeedIncludeTests(DatabaseFixture databaseFixture)
        : base(databaseFixture)
    {
    }

    private const string BundleOrderNumber = "PF-234-BUNDLE";
    private const string MenuOrderNumber = "PF-234-MENU";

    private static readonly Guid BundleParentItemId = Guid.NewGuid();
    private static readonly Guid BundleChildItemId = Guid.NewGuid();
    private static readonly Guid MenuIngredientId = Guid.NewGuid();

    /// <summary>
    /// A combo ("Menu Deal") with one nested component ("Fries"). The child is a row in the
    /// SAME Orders.Items collection — it carries the parent's OrderId and points back via
    /// ParentOrderItemId — which is exactly why the un-fixed mapper emitted it as its own
    /// top-level line while leaving the parent's SideItems null.
    /// </summary>
    [Fact]
    public async Task PrinterFeed_BundleOrder_NestsChildUnderParentInsteadOfEmittingItFlat()
    {
        var order = (await FetchFeedAsync()).Single(o => o.OrderNumber == BundleOrderNumber);

        // The child must NOT also appear as its own top-level line. Pre-fix this was 2:
        // once the SideItems include lands without root-only projection, the child prints
        // twice (nested AND flat).
        order.Items.Should().ContainSingle("only the bundle parent is a top-level line");
        var parent = order.Items.Single();
        parent.ProductName.Should().Be("Menu Deal");
        parent.Id.Should().Be(BundleParentItemId);

        // THE headline assertion: pre-fix SideItems was null on every printer-feed order,
        // so the kitchen ticket printed the component flat instead of nested.
        parent.SideItems.Should().NotBeNull("the bundle component must be nested under its parent");
        parent.SideItems!.Should().ContainSingle();

        var child = parent.SideItems!.Single();
        child.Id.Should().Be(BundleChildItemId);
        child.ProductName.Should().Be("Fries");
        child.Quantity.Should().Be(2);
        // Parent is ProductType.Menu, so its children are bundle components, not add-on sides.
        child.Kind.Should().Be(ItemKind.BundleChild);
    }

    /// <summary>
    /// Order.StatusHistory is initialized non-null on the entity, so the mapper's
    /// <c>?? new List&lt;&gt;()</c> fallback can never fire — omitting the include silently
    /// emitted an empty history rather than throwing.
    /// </summary>
    [Fact]
    public async Task PrinterFeed_Order_CarriesStatusHistory()
    {
        var order = (await FetchFeedAsync()).Single(o => o.OrderNumber == BundleOrderNumber);

        order.StatusHistory.Should().ContainSingle("the status transition was seeded and must survive mapping");
        var entry = order.StatusHistory.Single();
        entry.FromStatus.Should().Be(nameof(OrderStatus.Pending));
        entry.ToStatus.Should().Be(nameof(OrderStatus.Confirmed));
    }

    /// <summary>
    /// A menu-backed line carries MenuId with no ProductId, so both its KitchenType and its
    /// ingredient customizations resolve through Menu -&gt; MenuItems -&gt; Product. Without that
    /// include chain KitchenType came back null — and the printer app routes kitchen tickets by
    /// KitchenType (printer-app OrderPrintService.cs:99-101, :213), so the line printed on
    /// neither kitchen printer.
    /// </summary>
    [Fact]
    public async Task PrinterFeed_MenuBackedItem_ResolvesKitchenTypeAndIngredientsThroughMenu()
    {
        var order = (await FetchFeedAsync()).Single(o => o.OrderNumber == MenuOrderNumber);
        var item = order.Items.Single();

        item.MenuID.Should().NotBeNull("this line is menu-backed, not product-backed");
        item.ProductId.Should().BeNull();

        // Pre-fix both of these were null.
        item.KitchenType.Should().Be(nameof(Domain.Common.Enums.KitchenType.BackKitchen));
        item.IngredientCustomizations.Should().NotBeNull();
        var ingredient = item.IngredientCustomizations!.Single(i => i.IngredientId == MenuIngredientId);
        // Name resolved from GlobalIngredient.DefaultName proves the deepest include level ran.
        ingredient.IngredientName.Should().Be("Emmental");
        ingredient.IsRemoved.Should().BeTrue("it was deselected (qty 0) from the base recipe");
    }

    private async Task<List<OrderDto>> FetchFeedAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<CustomMediator>();
        return await mediator.SendQuery<FeedQuery, List<OrderDto>>(new FeedQuery(ModifiedSince: null));
    }

    protected override async Task SeedTestData()
    {
        await base.SeedTestData();

        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        context.AddRange(BuildBundleOrder());
        context.AddRange(BuildMenuBackedOrder());
        await context.SaveChangesAsync();
    }

    // A combo parent (ProductType.Menu) with one nested component. Both products carry a
    // KitchenType so the graph mirrors a real mixed-kitchen bundle.
    private static object[] BuildBundleOrder()
    {
        var comboProduct = NewProduct("Menu Deal", ProductType.Menu, Domain.Common.Enums.KitchenType.FrontKitchen);
        var friesProduct = NewProduct("Fries", ProductType.AddOn, Domain.Common.Enums.KitchenType.BackKitchen);

        var order = NewConfirmedOrder(BundleOrderNumber);
        order.Items.Add(new OrderItem
        {
            Id = BundleParentItemId,
            OrderId = order.Id,
            ProductId = comboProduct.Id,
            ProductName = "Menu Deal",
            Quantity = 1,
            UnitPrice = 18.00m,
            ItemTotal = 18.00m,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test"
        });
        order.Items.Add(new OrderItem
        {
            Id = BundleChildItemId,
            OrderId = order.Id,
            ProductId = friesProduct.Id,
            // The child is a sibling ROW that points at its parent — not a nested object graph.
            ParentOrderItemId = BundleParentItemId,
            ProductName = "Fries",
            Quantity = 2,
            UnitPrice = 3.50m,
            // Child rows carry UnitPrice for display but ItemTotal = 0: the parent's total
            // already rolls up the combo price (OrderItemFactory convention, issue #54).
            ItemTotal = 0m,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test"
        });
        order.StatusHistory.Add(new OrderStatusHistory
        {
            Id = Guid.NewGuid(),
            OrderId = order.Id,
            FromStatus = OrderStatus.Pending,
            ToStatus = OrderStatus.Confirmed,
            Notes = "confirmed by test",
            ChangedAt = DateTime.UtcNow,
            ChangedBy = "test",
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test"
        });

        return [comboProduct, friesProduct, order];
    }

    // A legacy menu-backed line: MenuId set, ProductId null. Everything the kitchen ticket
    // needs hangs off Menu -> MenuItems -> Product -> DetailedIngredients -> GlobalIngredient.
    private static object[] BuildMenuBackedOrder()
    {
        var globalCheese = new GlobalIngredient
        {
            Id = Guid.NewGuid(),
            DefaultName = "Emmental",
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test"
        };

        var product = NewProduct("Chief's Special", ProductType.MainItem, Domain.Common.Enums.KitchenType.BackKitchen);
        product.DetailedIngredients.Add(new ProductIngredient
        {
            Id = MenuIngredientId,
            ProductId = product.Id,
            Name = "Cheese",
            IsOptional = true,
            // Part of the base recipe, so deselecting it (qty 0) is a genuine removal.
            IsIncludedInBasePrice = true,
            GlobalIngredientId = globalCheese.Id,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test"
        });

        var menu = new Menu
        {
            Id = Guid.NewGuid(),
            Name = "Chief's Special",
            Date = new DateOnly(2026, 7, 27),
            BasePrice = 24.00m,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test"
        };
        menu.MenuItems.Add(new MenuItem
        {
            Id = Guid.NewGuid(),
            MenuId = menu.Id,
            ProductId = product.Id,
            Quantity = 1,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test"
        });

        var order = NewConfirmedOrder(MenuOrderNumber);
        order.Items.Add(new OrderItem
        {
            Id = Guid.NewGuid(),
            OrderId = order.Id,
            MenuId = menu.Id,
            ProductName = "Chief's Special",
            Quantity = 1,
            UnitPrice = 24.00m,
            ItemTotal = 24.00m,
            IngredientQuantitiesJson = System.Text.Json.JsonSerializer.Serialize(
                new Dictionary<Guid, int> { [MenuIngredientId] = 0 }),
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test"
        });

        return [globalCheese, product, menu, order];
    }

    private static Product NewProduct(string name, ProductType type, KitchenType kitchenType) => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        BasePrice = 10.00m,
        Type = type,
        KitchenType = kitchenType,
        Ingredients = [],
        Allergens = [],
        CreatedAt = DateTime.UtcNow,
        CreatedBy = "test"
    };

    // The feed only returns Confirmed orders (PrinterFeedQuery filters on it).
    private static Order NewConfirmedOrder(string orderNumber) => new()
    {
        Id = Guid.NewGuid(),
        OrderNumber = orderNumber,
        Type = OrderType.DineIn,
        Status = OrderStatus.Confirmed,
        PaymentStatus = PaymentStatus.Pending,
        OrderDate = DateTime.UtcNow,
        CreatedAt = DateTime.UtcNow,
        CreatedBy = "test"
    };
}
