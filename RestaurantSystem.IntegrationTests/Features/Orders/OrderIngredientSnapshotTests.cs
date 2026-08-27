using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RestaurantSystem.Api.Common;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Infrastructure;
using FeedQuery = RestaurantSystem.Api.Features.Orders.Queries.PrinterFeedQuery.PrinterFeedQuery;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

/// <summary>
/// Slice S1 of SHARED-MODIFIERS-AND-SAUCES-PLAN, decision D2 (owner-confirmed 2026-08-24):
/// <b>a past receipt never changes.</b>
/// </summary>
/// <remarks>
/// <para>
/// Every other part of an order line was already frozen — product name, variation name, unit price,
/// line total are snapshot columns (OrderItem.cs:20-24). The ingredient half was not: it was a bare
/// <c>Guid -&gt; int</c> map re-resolved against the LIVE catalog on every read, so an admin renaming
/// or deleting an ingredient rewrote a receipt that had already been printed. These tests pin the
/// three halves of the fix: the write freezes what was rendered, a later catalog edit cannot reach
/// it, and a line written BEFORE the snapshot existed still renders exactly as it did (there is no
/// backfill, by design).
/// </para>
/// <para>
/// The rename/delete assertions compare SERIALISED JSON rather than field by field. Byte-identical
/// is the claim the plan makes (§8), so byte-identical is what is asserted; a field-wise comparison
/// would pass while the order of the lines silently changed.
/// </para>
/// </remarks>
[Collection("Database Lane 3")]
public class OrderIngredientSnapshotTests : IntegrationTestBase
{
    public OrderIngredientSnapshotTests(DatabaseFixture databaseFixture)
        : base(databaseFixture)
    {
    }

    private const string CheeseName = "Cheese";
    private const string SauceName = "Tomato Sauce";
    private const string BaconName = "Extra Bacon";

    private static readonly Guid ProductId = Guid.NewGuid();
    private static readonly Guid CheeseId = Guid.NewGuid();
    private static readonly Guid SauceId = Guid.NewGuid();
    private static readonly Guid BaconId = Guid.NewGuid();

    /// <summary>The line the guest built: cheese kept, sauce taken off, two rashers of bacon added.</summary>
    private static Dictionary<Guid, int> GuestChoice() => new()
    {
        [CheeseId] = 1,
        [SauceId] = 0,
        [BaconId] = 2
    };

    // ── The write ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Checkout_FreezesTheRenderedIngredientLinesOnTheOrder()
    {
        var orderId = await CheckoutAsync("S1-FREEZE");

        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var frozen = await context.Set<OrderItemIngredient>()
            .Where(row => row.OrderItem.OrderId == orderId)
            .OrderBy(row => row.SortOrder)
            .ToListAsync();

        frozen.Should().HaveCount(3, "the line renders all three recipe rows, chosen and removed alike");

        frozen[0].IngredientName.Should().Be(CheeseName);
        frozen[0].Quantity.Should().Be(1);
        frozen[0].IsRemoved.Should().BeFalse();

        frozen[1].IngredientName.Should().Be(SauceName);
        frozen[1].Quantity.Should().Be(0);
        frozen[1].IsRemoved.Should().BeTrue("an explicit 0 on a base-recipe ingredient is a removal");

        frozen[2].IngredientName.Should().Be(BaconName);
        frozen[2].Quantity.Should().Be(2);
        frozen[2].IsRemoved.Should().BeFalse("a paid add-on at qty 2 was added, not removed");

        // Provenance is recorded even though no reader ever resolves it.
        frozen.Select(row => row.IngredientId).Should().Equal(CheeseId, SauceId, BaconId);
    }

    /// <summary>
    /// The snapshot records what was CHARGED nowhere, and that is deliberate. Ingredient money has
    /// exactly one writer — BasketPricingService.cs:97-159 — and what reaches an order is already an
    /// aggregate (BasketToOrderTranslator.cs:137-138 → OrderItemFactory.cs:242-243 → ItemTotal).
    /// Storing a per-ingredient amount here would mean re-deriving it at checkout from the live
    /// catalog: a second price authority whose sum need not equal what the guest actually paid.
    /// </summary>
    [Fact]
    public void Snapshot_CarriesNoMoney_SoItCannotBecomeASecondPriceAuthority()
    {
        typeof(OrderItemIngredient).GetProperties()
            .Where(p => p.PropertyType == typeof(decimal) || p.PropertyType == typeof(decimal?))
            .Should().BeEmpty(
                "ingredient pricing has one writer (BasketPricingService.cs:97-159); a money column "
                + "on the order-line snapshot would be a second one");
    }

    // ── The freeze holds ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task RenamingTheIngredientAfterCheckout_LeavesTheOrderLineByteIdentical()
    {
        var orderId = await CheckoutAsync("S1-RENAME");
        var before = await RenderIngredientLinesAsync(orderId);

        await MutateCatalogAsync(async context =>
        {
            var cheese = await context.ProductIngredients.SingleAsync(pi => pi.Id == CheeseId);
            cheese.Name = "Mozzarella di Bufala";
        });

        var after = await RenderIngredientLinesAsync(orderId);

        after.Should().Be(before, "a past receipt never changes (D2)");
        after.Should().Contain(CheeseName, "the frozen word is what was printed, not the new one");
        after.Should().NotContain("Mozzarella");
    }

    [Fact]
    public async Task DeletingTheIngredientAfterCheckout_LeavesTheOrderLineByteIdentical()
    {
        var orderId = await CheckoutAsync("S1-DELETE");
        var before = await RenderIngredientLinesAsync(orderId);

        await MutateCatalogAsync(async context =>
        {
            var recipe = await context.ProductIngredients.Where(pi => pi.ProductId == ProductId).ToListAsync();
            context.ProductIngredients.RemoveRange(recipe);
        });

        var after = await RenderIngredientLinesAsync(orderId);

        after.Should().Be(before,
            "the whole recipe is gone — pre-S1 this line rendered nothing at all (the all-orphan guard)");
    }

    /// <summary>
    /// The printer feed is a separate query with its own include chain, and it is AsNoTracking — so
    /// unlike the tracked paths there is no EF relationship fix-up to accidentally supply a missing
    /// include. Driving the real handler is what proves the snapshot is actually loaded there.
    /// </summary>
    [Fact]
    public async Task PrinterFeed_ServesTheFrozenLines_UnchangedByALaterCatalogRename()
    {
        var orderId = await CheckoutAsync("S1-PRINTER", OrderStatus.Confirmed);
        var before = await FetchPrinterLinesAsync("S1-PRINTER");

        await MutateCatalogAsync(async context =>
        {
            var cheese = await context.ProductIngredients.SingleAsync(pi => pi.Id == CheeseId);
            cheese.Name = "Renamed After The Ticket Printed";
        });

        var after = await FetchPrinterLinesAsync("S1-PRINTER");

        after.Should().Be(before, "a kitchen ticket already printed cannot be reworded by a catalog edit");
        after.Should().Contain(CheeseName);
        orderId.Should().NotBeEmpty();
    }

    // ── No backfill: the historic shape still works ──────────────────────────────────────────

    /// <summary>
    /// S1 ships with NO migration of existing rows, so every order placed before it carries an id map
    /// and no snapshot. Those lines must keep resolving against the live recipe exactly as they did —
    /// including the S0n all-orphan guard, which is the only thing standing between a churned
    /// historic line and 147 false "NO X" removals (measured on prod, slice S0m).
    /// </summary>
    [Fact]
    public async Task HistoricLineWithNoSnapshot_StillRendersFromTheLiveRecipe()
    {
        var orderId = await SeedHistoricOrderAsync("S1-HISTORIC", GuestChoice());

        using (var scope = Factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            (await context.Set<OrderItemIngredient>().CountAsync(row => row.OrderItem.OrderId == orderId))
                .Should().Be(0, "the fixture is a pre-S1 row: id map only, nothing frozen");
        }

        var rendered = await RenderIngredientLinesAsync(orderId);

        rendered.Should().Contain(CheeseName);
        rendered.Should().Contain(SauceName);
        rendered.Should().Contain(BaconName);
    }

    [Fact]
    public async Task HistoricLineWithNoSnapshot_FollowsTheLiveRecipeWhenTheCatalogChanges()
    {
        var orderId = await SeedHistoricOrderAsync("S1-HISTORIC-DRIFT", GuestChoice());
        var before = await RenderIngredientLinesAsync(orderId);

        await MutateCatalogAsync(async context =>
        {
            var cheese = await context.ProductIngredients.SingleAsync(pi => pi.Id == CheeseId);
            cheese.Name = "Renamed Cheese";
        });

        var after = await RenderIngredientLinesAsync(orderId);

        after.Should().NotBe(before);
        after.Should().Contain("Renamed Cheese",
            "unchanged behaviour, stated rather than assumed: a historic line has nothing frozen to "
            + "read, so it still follows the catalog. Only a backfill could change that, and S1 "
            + "deliberately performs none");
    }

    [Fact]
    public async Task HistoricLineWhoseIdsAllDied_StillSaysNothingRatherThanFalseRemovals()
    {
        var orderId = await SeedHistoricOrderAsync(
            "S1-HISTORIC-ORPHAN",
            new Dictionary<Guid, int> { [Guid.NewGuid()] = 1, [Guid.NewGuid()] = 0 });

        var dto = await RenderOrderAsync(orderId);

        dto.Items.Single().IngredientCustomizations.Should().BeNull(
            "the S0n all-orphan guard still owns the historic path");
    }

    // ── helpers ──────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Checks out through the REAL writer (<see cref="IOrderItemFactory"/>), which is the single
    /// place an OrderItem is constructed — both the basket checkout and POST /api/orders land here.
    /// </summary>
    private async Task<Guid> CheckoutAsync(string orderNumber, OrderStatus status = OrderStatus.Pending)
    {
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var factory = scope.ServiceProvider.GetRequiredService<IOrderItemFactory>();

        var order = NewOrder(orderNumber, status);

        var error = await factory.AddItemAsync(
            order,
            new CreateOrderItemDto
            {
                ProductId = ProductId,
                Quantity = 1,
                UnitPrice = 18.00m,
                IngredientQuantities = GuestChoice()
            },
            itemsAreServerPriced: true,
            CancellationToken.None);

        error.Should().BeNull();

        context.Orders.Add(order);
        await context.SaveChangesAsync();
        return order.Id;
    }

    /// <summary>A pre-S1 order line: the id map, and no snapshot rows.</summary>
    private async Task<Guid> SeedHistoricOrderAsync(string orderNumber, Dictionary<Guid, int> savedQuantities)
    {
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var order = NewOrder(orderNumber, OrderStatus.Confirmed);
        order.Items.Add(new OrderItem
        {
            Id = Guid.NewGuid(),
            OrderId = order.Id,
            ProductId = ProductId,
            ProductName = "Margherita",
            Quantity = 1,
            UnitPrice = 18.00m,
            ItemTotal = 18.00m,
            IngredientQuantitiesJson = JsonSerializer.Serialize(savedQuantities),
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test"
        });

        context.Orders.Add(order);
        await context.SaveChangesAsync();
        return order.Id;
    }

    private static Order NewOrder(string orderNumber, OrderStatus status) => new()
    {
        Id = Guid.NewGuid(),
        OrderNumber = orderNumber,
        Type = OrderType.DineIn,
        Status = status,
        PaymentStatus = PaymentStatus.Pending,
        OrderDate = DateTime.UtcNow,
        CreatedAt = DateTime.UtcNow,
        CreatedBy = "test"
    };

    private async Task MutateCatalogAsync(Func<ApplicationDbContext, Task> mutate)
    {
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await mutate(context);
        await context.SaveChangesAsync();
    }

    private async Task<OrderDto> RenderOrderAsync(Guid orderId)
    {
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var mapper = scope.ServiceProvider.GetRequiredService<IOrderMappingService>();

        var order = await context.Orders.Include(o => o.Items).FirstAsync(o => o.Id == orderId);
        return await mapper.MapToOrderDtoAsync(order);
    }

    private async Task<string> RenderIngredientLinesAsync(Guid orderId) =>
        JsonSerializer.Serialize((await RenderOrderAsync(orderId)).Items.Single().IngredientCustomizations);

    private async Task<string> FetchPrinterLinesAsync(string orderNumber)
    {
        using var scope = Factory.Services.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<CustomMediator>();
        var feed = await mediator.SendQuery<FeedQuery, List<OrderDto>>(new FeedQuery(ModifiedSince: null));
        return JsonSerializer.Serialize(
            feed.Single(o => o.OrderNumber == orderNumber).Items.Single().IngredientCustomizations);
    }

    protected override async Task SeedTestData()
    {
        await base.SeedTestData();

        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var product = new Product
        {
            Id = ProductId,
            Name = "Margherita",
            BasePrice = 18.00m,
            Type = ProductType.MainItem,
            Ingredients = new List<string>(),
            Allergens = new List<string>(),
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test"
        };

        product.DetailedIngredients.Add(NewIngredient(CheeseId, CheeseName, isOptional: false, order: 0));
        // Optional but included in the base price, so a saved 0 is a genuine removal.
        product.DetailedIngredients.Add(NewIngredient(SauceId, SauceName, isOptional: true, order: 1, includedInBase: true));
        // A paid add-on: never "removed", only added.
        product.DetailedIngredients.Add(
            NewIngredient(BaconId, BaconName, isOptional: true, order: 2, price: 2.50m, maxQuantity: 3));

        context.Products.Add(product);
        await context.SaveChangesAsync();
    }

    private static ProductIngredient NewIngredient(
        Guid id,
        string name,
        bool isOptional,
        int order,
        bool includedInBase = false,
        decimal price = 0m,
        int maxQuantity = 1) => new()
        {
            Id = id,
            ProductId = ProductId,
            Name = name,
            IsOptional = isOptional,
            IsIncludedInBasePrice = includedInBase,
            IsActive = true,
            Price = price,
            MaxQuantity = maxQuantity,
            DisplayOrder = order,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test"
        };
}
