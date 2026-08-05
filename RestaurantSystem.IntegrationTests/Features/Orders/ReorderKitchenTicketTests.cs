using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Basket.Dtos;
using RestaurantSystem.Api.Features.Basket.Dtos.Requests;
using RestaurantSystem.Api.Features.Orders.Commands.CreateOrderFromBasketCommand;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Infrastructure;
using System.Net;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

// Issue #303: re-ordering a past order made the KITCHEN TICKET lie.
//
// `useReorder` posts product + quantity and nothing else — no selectedIngredients, no
// ingredientQuantities. LineCustomizationBuilder's regular-item branch backfilled anyway (it
// guarded only on the product having ingredients, unlike its bundle-child branch), writing an
// explicit quantity 0 for every unselected active ingredient that is optional or included in the
// base price. Those zeroes then travel basket → BasketToOrderTranslator → OrderItemFactory →
// OrderMappingService, which reads a saved 0 on a BASE-RECIPE ingredient as IsRemoved = true. The
// printer prints "- NO {name}" for each.
//
// The two sets are deliberately different, and the fixture below spans the difference: a paid
// add-on gets a 0 and is NOT a removal, while a required ingredient that is not flagged
// included-in-base gets no entry at all and is still reported removed — by OrderMappingService's
// separate required-absent branch.
//
// #363 gated the CART's removals on the line having carried a selection, so /cart, /menu and
// checkout were already honest — this suite covers the leg #363 routed around: the order and the
// ticket printed from it. It drives the real endpoints (POST /api/basket/items →
// POST /api/orders/from-basket) rather than the builder in isolation, because the whole claim is
// about a chain of four services agreeing on what a 0 means.
public class ReorderKitchenTicketTests : IntegrationTestBase
{
    private readonly string _sessionId = Guid.NewGuid().ToString();
    private Product _testPizza = null!;

    private ProductIngredient _cheese = null!;      // optional, INCLUDED in base → removable
    private ProductIngredient _mushrooms = null!;   // optional, NOT included (paid) → never a removal
    private ProductIngredient _tomatoSauce = null!; // required → removable; a backfill gives it NO entry
    private ProductIngredient _basil = null!;       // optional, included in base → removable

    public ReorderKitchenTicketTests(DatabaseFixture databaseFixture)
        : base(databaseFixture)
    {
    }

    protected override async Task SeedTestData()
    {
        await base.SeedTestData();

        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        _testPizza = await context.Products.FirstAsync(p => p.Name == "Test Pizza");

        _cheese = Ingredient("Cheese", isOptional: true, includedInBase: true, price: 1.00m, order: 1);
        _mushrooms = Ingredient("Mushrooms", isOptional: true, includedInBase: false, price: 2.00m, order: 2);
        _tomatoSauce = Ingredient("Tomato Sauce", isOptional: false, includedInBase: false, price: 0m, order: 3);
        _basil = Ingredient("Basil", isOptional: true, includedInBase: true, price: 0m, order: 4);

        context.ProductIngredients.AddRange(_cheese, _mushrooms, _tomatoSauce, _basil);
        await context.SaveChangesAsync();
    }

    private ProductIngredient Ingredient(string name, bool isOptional, bool includedInBase, decimal price, int order) =>
        new()
        {
            Id = Guid.NewGuid(),
            ProductId = _testPizza.Id,
            Name = name,
            IsOptional = isOptional,
            IsIncludedInBasePrice = includedInBase,
            Price = price,
            MaxQuantity = 2,
            IsActive = true,
            DisplayOrder = order,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test"
        };

    /// <summary>
    /// Checks out whatever is in the session basket through the real from-basket endpoint — the
    /// one the checkout page posts to — and returns the resulting order.
    /// </summary>
    private async Task<OrderDto> CheckoutAsync()
    {
        var response = await PostAsJsonAsync("/api/orders/from-basket", new CreateOrderFromBasketCommand
        {
            Type = OrderType.DineIn,
            TableNumber = 7,
            CustomerName = "Test Customer",
            Payments = new List<CreateOrderPaymentDto>
            {
                new() { PaymentMethod = PaymentMethod.Cash, Amount = 100.00m }
            }
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await ReadResponseAsync<ApiResponse<OrderDto>>(response);
        result!.Success.Should().BeTrue();
        return result.Data!;
    }

    private static OrderItemDto PizzaLine(OrderDto order, Guid pizzaId)
    {
        var line = order.Items.SingleOrDefault(i => i.ProductId == pizzaId);
        // Not an implementation detail of the assertion below — a null line would make every
        // "no removals" claim vacuously true.
        line.Should().NotBeNull("the order must contain the pizza that was checked out");
        return line!;
    }

    // ---- The defect ---------------------------------------------------------------------------

    // THE REORDER PAYLOAD, all the way to the order. Product + quantity, nothing else: exactly what
    // useReorder sends. Nobody customized this pizza, so nothing on it was removed.
    //
    // Before the fix this failed naming Cheese, Basil AND Tomato Sauce — the last one via
    // OrderMappingService's separate "required ingredient absent from the map = removed" branch,
    // which the backfill reaches by writing entries for the optional ones only.
    [Fact]
    public async Task ReorderStyleAdd_DoesNotFabricateRemovalsOnTheOrder()
    {
        Client.DefaultRequestHeaders.Add("X-Session-Id", _sessionId);

        var add = await PostAsJsonAsync("/api/basket/items", new AddToBasketDto
        {
            ProductId = _testPizza.Id,
            Quantity = 1
        });
        add.StatusCode.Should().Be(HttpStatusCode.OK);

        var order = await CheckoutAsync();
        var line = PizzaLine(order, _testPizza.Id);

        var fabricated = (line.IngredientCustomizations ?? new List<OrderItemIngredientDto>())
            .Where(i => i.IsRemoved)
            .Select(i => i.IngredientName)
            .ToList();

        // BeEquivalentTo over an empty set rather than BeEmpty: on failure it prints the WHOLE
        // fabricated list, which is the measurement this test exists to produce.
        fabricated.Should().BeEquivalentTo(Array.Empty<string>(),
            "the guest expressed no selection, so nothing on this pizza was removed");
    }

    // The same claim one layer down, on the persisted column the printer feed re-reads long after
    // the create-order response is gone. A stored map of zeroes is a lie that outlives the request:
    // every later GET of this order — the order view, the printer poll — re-derives IsRemoved from
    // it. Asserting the DTO alone would leave a fix that merely filtered the response looking
    // complete.
    //
    // Orders already written keep their fabricated maps, and that is not fixable: OrderItem stores
    // IngredientQuantitiesJson and no selection column, so a fabricated map is byte-identical to a
    // genuine "the guest took everything off" — which is the whole defect. Any retro-fix would have
    // to guess, and guessing wrong erases a real removal from a real ticket. Forward-only.
    [Fact]
    public async Task ReorderStyleAdd_StoresNoFabricatedQuantitiesOnTheOrderRow()
    {
        Client.DefaultRequestHeaders.Add("X-Session-Id", _sessionId);

        (await PostAsJsonAsync("/api/basket/items", new AddToBasketDto
        {
            ProductId = _testPizza.Id,
            Quantity = 1
        })).StatusCode.Should().Be(HttpStatusCode.OK);

        var order = await CheckoutAsync();

        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var row = await context.OrderItems
            .SingleAsync(i => i.OrderId == order.Id && i.ProductId == _testPizza.Id);

        row.IngredientQuantitiesJson.Should().BeNullOrEmpty(
            "a line the guest never customized has no ingredient choices to snapshot");
    }

    // THE SAME DEFECT ON THE MONEY PATH, and the reason the ticket fix cannot ship alone.
    // LineCustomization carries three fields off one payload, and the customization PRICE is
    // computed from the same null selection: BasketPricingService reads "not selected" for every
    // optional ingredient, and for one that is included in the base price that means deduct it
    // (Customization_NullSelected_TreatsAllAsDeselected pins that rule, correctly — the rule is
    // right, the caller's input was not). So a re-ordered pizza was billed as though the cheese had
    // been taken off. Fixing only the JSON would have left the charge wrong AND removed the "NO
    // Cheese" line that was the only visible sign of it.
    [Fact]
    public async Task ReorderStyleAdd_IsNotDiscountedAsIfIngredientsWereRemoved()
    {
        Client.DefaultRequestHeaders.Add("X-Session-Id", _sessionId);

        var add = await PostAsJsonAsync("/api/basket/items", new AddToBasketDto
        {
            ProductId = _testPizza.Id,
            Quantity = 1
        });
        add.StatusCode.Should().Be(HttpStatusCode.OK);

        var basket = await ReadResponseAsync<ApiResponse<BasketDto>>(add);
        var cartLine = basket!.Data!.Items.Single(i => i.ProductId == _testPizza.Id);

        // The premise: cheese is priced and included in the base, so a spurious "deselected" verdict
        // on it is worth real money. Without this the 0 below could hold for a free ingredient.
        _cheese.Price.Should().BeGreaterThan(0m);
        _cheese.IsIncludedInBasePrice.Should().BeTrue();

        cartLine.CustomizationPrice.Should().Be(0m, "the guest expressed no ingredient choice to price");

        var order = await CheckoutAsync();
        PizzaLine(order, _testPizza.Id).ItemTotal.Should().Be(_testPizza.BasePrice,
            "a plain re-ordered pizza costs what the pizza costs");
    }

    // ---- What must NOT change -------------------------------------------------------------------

    // The positive control, and the reason this suite is not just an assertion that the feature is
    // off: a REAL removal still reaches the ticket. Cheese is deselected deliberately (it is
    // optional but included in the base price, so it was on the pizza), and the guest's selection
    // says so.
    //
    // This is also what stops the fix being over-broad: a guard that gated on ingredientQuantities
    // instead of the selection would pass the two tests above and fail this one, because it makes
    // the backfill arm dead code.
    //
    // The payload is selection-only, which is the arm under test but NOT what the live sheet sends:
    // `useItemCustomizationSheet.addToCart` posts BOTH, since `buildBaseIngredientSelection` seeds a
    // quantity of 1 for every base-recipe ingredient and a deselect writes an explicit 0. That shape
    // takes the verbatim arm and is covered by ExplicitQuantityMap_WithoutASelection_StillReachesTheOrder
    // plus the sheet-shaped case below.
    [Fact]
    public async Task DeselectedBaseRecipeIngredient_IsStillReportedRemovedOnTheOrder()
    {
        Client.DefaultRequestHeaders.Add("X-Session-Id", _sessionId);

        (await PostAsJsonAsync("/api/basket/items", new AddToBasketDto
        {
            ProductId = _testPizza.Id,
            Quantity = 1,
            // Cheese left out on purpose; everything else in the base recipe kept.
            SelectedIngredients = [_tomatoSauce.Id, _basil.Id]
        })).StatusCode.Should().Be(HttpStatusCode.OK);

        var order = await CheckoutAsync();
        var line = PizzaLine(order, _testPizza.Id);

        var removed = (line.IngredientCustomizations ?? new List<OrderItemIngredientDto>())
            .Where(i => i.IsRemoved)
            .Select(i => i.IngredientName)
            .ToList();

        removed.Should().Contain("Cheese");
        // The paid add-on nobody chose carries a saved 0 exactly as cheese does, and is still not a
        // removal — IngredientRecipeRules' base-recipe rule, unchanged by this fix.
        removed.Should().NotContain("Mushrooms");
    }

    // THE SHAPE THE LIVE SHEET ACTUALLY SENDS — selection AND a non-empty quantity map, because
    // buildBaseIngredientSelection seeds a 1 for every base-recipe ingredient and a deselect writes
    // an explicit 0. It takes the verbatim arm, so neither half of this fix touches it; pinned here
    // because it is the one payload a real customer generates, and a change to the guard that broke
    // it would otherwise be caught only by the sheet-less shapes above.
    //
    // The price assertion is the guard against an over-broad price gate: a real deselect must STILL
    // be discounted. The #303 gate turns on whether the payload expressed a choice, not on what the
    // choice was.
    [Fact]
    public async Task SheetShapedAdd_ReportsTheRemovalAndDiscountsIt()
    {
        Client.DefaultRequestHeaders.Add("X-Session-Id", _sessionId);

        var add = await PostAsJsonAsync("/api/basket/items", new AddToBasketDto
        {
            ProductId = _testPizza.Id,
            Quantity = 1,
            SelectedIngredients = [_tomatoSauce.Id, _basil.Id],
            IngredientQuantities = new Dictionary<Guid, int>
            {
                [_cheese.Id] = 0,
                [_tomatoSauce.Id] = 1,
                [_basil.Id] = 1
            }
        });
        add.StatusCode.Should().Be(HttpStatusCode.OK);

        var basket = await ReadResponseAsync<ApiResponse<BasketDto>>(add);
        basket!.Data!.Items.Single(i => i.ProductId == _testPizza.Id)
            .CustomizationPrice.Should().Be(-_cheese.Price, "the guest really did take the cheese off");

        var order = await CheckoutAsync();
        var line = PizzaLine(order, _testPizza.Id);

        line.IngredientCustomizations.Should().NotBeNull();
        line.IngredientCustomizations!
            .Where(i => i.IsRemoved)
            .Select(i => i.IngredientName)
            .Should().BeEquivalentTo(["Cheese"]);
    }

    // An explicit quantity map with no selection list is the other regular-item shape the builder
    // accepts (its first arm persists a provided map verbatim). It is a real expression of choice,
    // so it must survive the guard — which is placed on the BACKFILL arm alone.
    [Fact]
    public async Task ExplicitQuantityMap_WithoutASelection_StillReachesTheOrder()
    {
        Client.DefaultRequestHeaders.Add("X-Session-Id", _sessionId);

        (await PostAsJsonAsync("/api/basket/items", new AddToBasketDto
        {
            ProductId = _testPizza.Id,
            Quantity = 1,
            IngredientQuantities = new Dictionary<Guid, int> { [_cheese.Id] = 0 }
        })).StatusCode.Should().Be(HttpStatusCode.OK);

        var order = await CheckoutAsync();
        var line = PizzaLine(order, _testPizza.Id);

        line.IngredientCustomizations.Should().NotBeNull();
        line.IngredientCustomizations!
            .Where(i => i.IsRemoved)
            .Select(i => i.IngredientName)
            .Should().Contain("Cheese");
    }
}
