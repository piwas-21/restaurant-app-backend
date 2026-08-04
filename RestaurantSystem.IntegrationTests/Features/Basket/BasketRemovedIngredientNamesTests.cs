using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Basket.Dtos;
using RestaurantSystem.Api.Features.Basket.Dtos.Requests;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Infrastructure;
using System.Net;

namespace RestaurantSystem.IntegrationTests.Features.Basket;

// Issue #363: the cart could never show removed ingredients while the order view could, because
// they read different channels. The cart's `removed` came from `excludedIngredientNames`, derived
// from a column that was never written (#170 / backend #283); the order's came from `isRemoved`,
// which OrderMappingService derives from a quantity of 0. So a guest who removed an ingredient saw
// it listed once the order existed and never in the cart they were checking out from.
//
// Fixed by giving the cart the SAME channel through the SAME rule: BasketMappingService now emits
// RemovedIngredientNames via IngredientRecipeRules, which is also what OrderMappingService calls.
//
// THE TEST THAT MATTERS MOST IS THE NEGATIVE ONE. The issue proposed deriving removals from
// `ingredientQuantities[id] === 0` in the frontend, and that is wrong: LineCustomizationBuilder
// writes an explicit 0 for EVERY unselected optional ingredient, including paid add-ons nobody
// asked for. A plain pizza's saved quantities carry a 0 for mushrooms. Reading that naively puts
// "No Mushrooms" on a cart line for a topping the guest never touched, so
// UnselectedPaidAddOn_IsNotReportedAsRemoved is what separates this fix from that one.
public class BasketRemovedIngredientNamesTests : IntegrationTestBase
{
    private readonly string _sessionId = Guid.NewGuid().ToString();
    private Product _testPizza = null!;
    private Product _testCola = null!;
    private Product _menuProduct = null!;
    private MenuSection _mainSection = null!;
    private MenuSection _drinkSection = null!;

    private ProductIngredient _cheese = null!;      // optional, INCLUDED in base → removable
    private ProductIngredient _mushrooms = null!;   // optional, NOT included (paid) → never a removal
    private ProductIngredient _tomatoSauce = null!; // required → removable
    private ProductIngredient _basil = null!;       // optional+included, carries a GlobalIngredient

    private const string BasilGlobalName = "Fresh Basil";
    private const string BasilProductName = "Basil (product-local name)";

    public BasketRemovedIngredientNamesTests(DatabaseFixture databaseFixture)
        : base(databaseFixture)
    {
    }

    protected override async Task SeedTestData()
    {
        await base.SeedTestData();

        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        _testPizza = await context.Products.FirstAsync(p => p.Name == "Test Pizza");
        _testCola = await context.Products.FirstAsync(p => p.Name == "Test Cola");

        _cheese = Ingredient("Cheese", isOptional: true, includedInBase: true, price: 1.00m, order: 1);
        _mushrooms = Ingredient("Mushrooms", isOptional: true, includedInBase: false, price: 2.00m, order: 2);
        _tomatoSauce = Ingredient("Tomato Sauce", isOptional: false, includedInBase: false, price: 0m, order: 3);
        _basil = Ingredient(BasilProductName, isOptional: true, includedInBase: true, price: 0m, order: 4);

        // A global ingredient behind basil ONLY. DisplayName prefers its DefaultName over the
        // per-product Name, matching the order snapshot — and the basket query has to eager-load
        // it or the two surfaces print different words for one thing.
        var globalBasil = new GlobalIngredient
        {
            Id = Guid.NewGuid(),
            DefaultName = BasilGlobalName,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test"
        };
        context.GlobalIngredients.Add(globalBasil);
        _basil.GlobalIngredientId = globalBasil.Id;

        context.ProductIngredients.AddRange(_cheese, _mushrooms, _tomatoSauce, _basil);

        var menuProduct = new Product
        {
            Id = Guid.NewGuid(),
            Name = "Customizable Combo",
            BasePrice = 8.00m,
            IsActive = true,
            IsAvailable = true,
            PreparationTimeMinutes = 20,
            Type = ProductType.Menu,
            Ingredients = new List<string>(),
            Allergens = new List<string>(),
            DisplayOrder = 20,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test"
        };

        var menuDefinition = new MenuDefinition
        {
            Id = Guid.NewGuid(),
            ProductId = menuProduct.Id,
            IsAlwaysAvailable = true,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test"
        };

        _mainSection = Section(menuDefinition.Id, "Main", 1);
        _drinkSection = Section(menuDefinition.Id, "Drink", 2);
        _mainSection.Items.Add(SectionItem(_mainSection.Id, _testPizza.Id, 2.99m));
        _drinkSection.Items.Add(SectionItem(_drinkSection.Id, _testCola.Id, 1.99m));

        menuDefinition.Sections.Add(_mainSection);
        menuDefinition.Sections.Add(_drinkSection);
        menuProduct.MenuDefinition = menuDefinition;

        context.Products.Add(menuProduct);
        await context.SaveChangesAsync();

        _menuProduct = menuProduct;
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

    private static MenuSection Section(Guid definitionId, string name, int order) =>
        new()
        {
            Id = Guid.NewGuid(),
            MenuDefinitionId = definitionId,
            Name = name,
            DisplayOrder = order,
            IsRequired = true,
            MinSelection = 1,
            MaxSelection = 1,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test"
        };

    private static MenuSectionItem SectionItem(Guid sectionId, Guid productId, decimal additional) =>
        new()
        {
            Id = Guid.NewGuid(),
            MenuSectionId = sectionId,
            ProductId = productId,
            AdditionalPrice = additional,
            DisplayOrder = 1,
            IsDefault = true,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test"
        };

    /// <summary>
    /// Adds the pizza as a PLAIN product with the given selection, and returns its cart line.
    /// </summary>
    private async Task<BasketItemDto> AddPizzaAsync(
        List<Guid> selectedIngredients,
        Dictionary<Guid, int>? quantities = null)
    {
        var response = await PostAsJsonAsync("/api/basket/items", new AddToBasketDto
        {
            ProductId = _testPizza.Id,
            Quantity = 1,
            SelectedIngredients = selectedIngredients,
            IngredientQuantities = quantities
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var basket = await ReadResponseAsync<ApiResponse<BasketDto>>(response);
        var line = basket!.Data!.Items.FirstOrDefault(i => i.ProductId == _testPizza.Id);
        line.Should().NotBeNull();
        return line!;
    }

    // ---- The defect --------------------------------------------------------------------------

    // Cheese is optional but included in the base price, so it was on the pizza and taking it off
    // is a real removal — the "No cheese" the order view has always shown and the cart never did.
    [Fact]
    public async Task RemovedBaseRecipeIngredient_IsReportedToTheCart()
    {
        Client.DefaultRequestHeaders.Add("X-Session-Id", _sessionId);

        var line = await AddPizzaAsync([_tomatoSauce.Id, _basil.Id]);

        line.RemovedIngredientNames.Should().NotBeNull();
        line.RemovedIngredientNames.Should().Contain("Cheese");
    }

    // THE ONE THAT SEPARATES THIS FIX FROM THE NAIVE ONE. Mushrooms are optional and NOT included
    // in the base price — a paid add-on. The guest never selected them, so the persisted
    // quantities carry mushrooms: 0 exactly as they carry cheese: 0. A rule that reads only the
    // quantity would print "No Mushrooms" for a topping that was never on the pizza.
    [Fact]
    public async Task UnselectedPaidAddOn_IsNotReportedAsRemoved()
    {
        Client.DefaultRequestHeaders.Add("X-Session-Id", _sessionId);

        var line = await AddPizzaAsync([_tomatoSauce.Id, _cheese.Id, _basil.Id]);

        // The premise: the payload really does carry a zero for the add-on. Without this the test
        // could pass because nothing wrote a 0 at all, proving nothing about the rule.
        line.IngredientQuantities.Should().NotBeNull();
        line.IngredientQuantities![_mushrooms.Id].Should().Be(0);

        line.RemovedIngredientNames.Should().NotContain("Mushrooms");
    }

    // A required ingredient is in the base recipe by definition, so deselecting it IS a removal.
    [Fact]
    public async Task RemovedRequiredIngredient_IsReportedToTheCart()
    {
        Client.DefaultRequestHeaders.Add("X-Session-Id", _sessionId);

        var line = await AddPizzaAsync([_cheese.Id, _basil.Id], new Dictionary<Guid, int>
        {
            [_tomatoSauce.Id] = 0
        });

        line.RemovedIngredientNames.Should().Contain("Tomato Sauce");
    }

    // The cart names an ingredient by its PER-PRODUCT name, like every other name list on a cart
    // line and like the customization sheet, MenuCard and the POS sheet — all of which read
    // ProductIngredientDto, which exposes no global name at all.
    //
    // Pinned because the order snapshot does the opposite: OrderMappingService prefers
    // GlobalIngredient.DefaultName. So for an ingredient whose global and per-product names have
    // been allowed to diverge, the cart and the order still print different words. That is
    // pre-existing and applies equally to the selected/added lists this PR does not touch;
    // harmonizing it is a decision about which of the five surfaces is wrong, not part of #363.
    // Asserting the current answer keeps the next change to it deliberate.
    [Fact]
    public async Task RemovedIngredient_UsesTheProductLocalName()
    {
        Client.DefaultRequestHeaders.Add("X-Session-Id", _sessionId);

        var line = await AddPizzaAsync([_tomatoSauce.Id, _cheese.Id]);

        line.RemovedIngredientNames.Should().Contain(BasilProductName);
        line.RemovedIngredientNames.Should().NotContain(BasilGlobalName);
    }

    // THE RE-ORDER PAYLOAD. `useReorder` posts product + quantity and NOTHING else — no
    // selectedIngredients, no ingredientQuantities.
    //
    // This test used to assert, as its premise, that LineCustomizationBuilder backfilled a 0 for
    // every unselected active optional-or-included ingredient regardless — which it did, because its regular-item
    // branch guarded only on the product having ingredients while its bundle-child branch guarded
    // on the selection. The cart was honest only because BuildRemovedIngredientNames refused to
    // read a selection-less map. Those zeroes still reached the ORDER, so the kitchen ticket
    // printed "NO Cheese" for a re-ordered Margherita (#303, fixed at the builder).
    //
    // So the premise is inverted, not dropped: nothing is written at all now. The gate this test
    // was named for is no longer what saves the re-order payload — see
    // QuantitiesWithoutASelection_ReportNoRemovals for what still exercises it, and
    // ReorderKitchenTicketTests for the order/kitchen-ticket leg.
    [Fact]
    public async Task ReorderStyleAdd_WithNoSelection_ReportsNoRemovals()
    {
        Client.DefaultRequestHeaders.Add("X-Session-Id", _sessionId);

        var response = await PostAsJsonAsync("/api/basket/items", new AddToBasketDto
        {
            ProductId = _testPizza.Id,
            Quantity = 1
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var basket = await ReadResponseAsync<ApiResponse<BasketDto>>(response);
        var line = basket!.Data!.Items.Single(i => i.ProductId == _testPizza.Id);

        // THE PREMISE, and it has to be measured rather than echoed: both assertions below are
        // "nothing is there", so they hold just as well for a pizza with no ingredients at all.
        // Counting the rows a backfill WOULD have zeroed is what makes a null answer meaningful.
        //
        // It does NOT rule out the other way this could go quietly vacuous — the add path dropping
        // its `.Include(p => p.DetailedIngredients)`, which would starve the backfill of input
        // while this count, on its own DbContext, still reported 3. The sibling
        // UnselectedPaidAddOn_IsNotReportedAsRemoved is what would fail then, because it asserts a
        // backfilled 0 is present in the response.
        using (var scope = Factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var backfillable = await context.ProductIngredients.CountAsync(pi =>
                pi.ProductId == _testPizza.Id && pi.IsActive && (pi.IsOptional || pi.IsIncludedInBasePrice));
            backfillable.Should().BeGreaterThan(0,
                "otherwise there is nothing the builder could have fabricated and this proves nothing");
        }

        line.IngredientQuantities.Should().BeNull(
            "a payload that expressed no ingredient choice must not be given one (#303)");
        line.RemovedIngredientNames.Should().BeNull("the guest expressed no selection, so nothing was removed");
    }

    // The gate itself, on the one payload that can still reach it: an explicit quantity map posted
    // WITHOUT a selection list. That is persisted verbatim by the builder's first arm — #303 gated
    // the backfill, not this — so the cart really does hold a zero-for-a-base-recipe-ingredient
    // here, and declines to call it a removal because no selection vouches for it.
    //
    // Pinned because it is the divergence: the ORDER view reads the same saved 0 as a removal
    // (ReorderKitchenTicketTests.ExplicitQuantityMap_WithoutASelection_StillReachesTheOrder). No
    // live producer posts this shape — the customization sheet always sends a selection — and
    // which of the two is right is the standing cart-vs-order question in IngredientRecipeRules'
    // remarks, not something to settle here.
    [Fact]
    public async Task QuantitiesWithoutASelection_ReportNoRemovals()
    {
        Client.DefaultRequestHeaders.Add("X-Session-Id", _sessionId);

        var response = await PostAsJsonAsync("/api/basket/items", new AddToBasketDto
        {
            ProductId = _testPizza.Id,
            Quantity = 1,
            IngredientQuantities = new Dictionary<Guid, int> { [_cheese.Id] = 0 }
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var basket = await ReadResponseAsync<ApiResponse<BasketDto>>(response);
        var line = basket!.Data!.Items.Single(i => i.ProductId == _testPizza.Id);

        // The premise: the map really is on the line, and it really does carry a 0 for an
        // ingredient the base-recipe rule would otherwise call removed.
        line.IngredientQuantities.Should().NotBeNull();
        line.IngredientQuantities![_cheese.Id].Should().Be(0);
        // BeNull, not BeNullOrEmpty: the gate tests for null specifically, and an EMPTY selection
        // would fall straight through it and report ["Cheese"].
        line.SelectedIngredients.Should().BeNull();

        line.RemovedIngredientNames.Should().BeNull("no selection vouches for the saved 0");
    }

    // A line nobody customized says nothing, rather than adding an empty array to every plain item
    // in every cart response.
    [Fact]
    public async Task LineForProductWithNoIngredients_ReportsNoRemovals()
    {
        Client.DefaultRequestHeaders.Add("X-Session-Id", _sessionId);

        var response = await PostAsJsonAsync("/api/basket/items", new AddToBasketDto
        {
            ProductId = _testCola.Id,
            Quantity = 1
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var basket = await ReadResponseAsync<ApiResponse<BasketDto>>(response);
        var line = basket!.Data!.Items.Single(i => i.ProductId == _testCola.Id);

        line.RemovedIngredientNames.Should().BeNull();
    }

    // ---- Bundle components, which previously carried NO names at all --------------------------

    // The child mapping was a reduced copy of the root's and set no name lists at all, so a bundle
    // component's removals were unreportable however the guest had edited it.
    //
    // Only removals are asserted, because only removals are populated: see MapChildItem for why
    // SelectedIngredientNames is deliberately still absent on a child.
    [Fact]
    public async Task BundleChild_ReportsItsOwnRemovals()
    {
        Client.DefaultRequestHeaders.Add("X-Session-Id", _sessionId);

        var response = await PostAsJsonAsync("/api/basket/items", new AddToBasketDto
        {
            ProductId = _menuProduct.Id,
            Quantity = 1,
            SelectedMenuOptions =
            [
                new SelectedMenuOptionDto
                {
                    SectionId = _mainSection.Id,
                    ItemId = _testPizza.Id,
                    Quantity = 1,
                    // Cheese AND basil deselected; mushrooms bought. Basil is deselected on purpose:
                    // the name assertion below is only meaningful if basil is actually removed.
                    SelectedIngredients = [_tomatoSauce.Id, _mushrooms.Id],
                    IngredientQuantities = new Dictionary<Guid, int> { [_mushrooms.Id] = 1 }
                },
                new SelectedMenuOptionDto { SectionId = _drinkSection.Id, ItemId = _testCola.Id, Quantity = 1 }
            ]
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var basket = await ReadResponseAsync<ApiResponse<BasketDto>>(response);
        var parent = basket!.Data!.Items.Single(i => i.ProductId == _menuProduct.Id);
        var pizzaChild = parent.ChildItems!.Single(c => c.ProductId == _testPizza.Id);

        pizzaChild.RemovedIngredientNames.Should().NotBeNull();
        pizzaChild.RemovedIngredientNames.Should().Contain("Cheese");
        // The paid add-on the guest DID buy is not a removal either.
        pizzaChild.RemovedIngredientNames.Should().NotContain("Mushrooms");

        // Basil was deselected too, and resolves by its per-product name here exactly as it does on
        // a root line — which is also what proves the CHILD product's DetailedIngredients are
        // eager-loaded, since an un-included collection would yield an empty list, not a wrong name.
        pizzaChild.RemovedIngredientNames.Should().Contain(BasilProductName);

        // Still deliberately unset on a child — the added side is not #363's subject, and the cart
        // pairs it positionally. Asserted so removing this restraint is a visible decision.
        pizzaChild.SelectedIngredientNames.Should().BeNull();
    }

    // The uncustomized component of the same bundle stays quiet — a child with no quantities has
    // nothing to report, and the recursion must not invent rows for it.
    [Fact]
    public async Task BundleChildForProductWithNoIngredients_ReportsNoRemovals()
    {
        Client.DefaultRequestHeaders.Add("X-Session-Id", _sessionId);

        var response = await PostAsJsonAsync("/api/basket/items", new AddToBasketDto
        {
            ProductId = _menuProduct.Id,
            Quantity = 1,
            SelectedMenuOptions =
            [
                new SelectedMenuOptionDto { SectionId = _mainSection.Id, ItemId = _testPizza.Id, Quantity = 1 },
                new SelectedMenuOptionDto { SectionId = _drinkSection.Id, ItemId = _testCola.Id, Quantity = 1 }
            ]
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var basket = await ReadResponseAsync<ApiResponse<BasketDto>>(response);
        var parent = basket!.Data!.Items.Single(i => i.ProductId == _menuProduct.Id);
        var colaChild = parent.ChildItems!.Single(c => c.ProductId == _testCola.Id);

        colaChild.RemovedIngredientNames.Should().BeNull();
    }
}
