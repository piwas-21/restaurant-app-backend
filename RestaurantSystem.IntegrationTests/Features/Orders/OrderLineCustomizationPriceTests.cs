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

// Issue #312: the order's rows disagreed with the basket they were built from, in two opposite
// directions, because BasketToOrderTranslator copied CustomizationPrice straight through.
//
// CreateOrderItemDto declares that field as the total for ALL quantities and OrderItemFactory honours
// it — `(UnitPrice * Quantity) + CustomizationPrice`. The BASKET stores it two different ways
// (BasketLineTotal): per unit for a regular row, folded into UnitPrice for a bundle row. So the same
// copy was 5.98 UNDER on a regular line at qty 3 and 3.00 OVER on a customised bundle at qty 2.
//
// WHY THESE TESTS EXIST AT ALL, given the basket suites are green: every basket assertion is about
// the BASKET row, and no order test combined the two factors the defect needs. It takes quantity > 1
// AND a non-zero CustomizationPrice together: BasketToOrderIntegrationTest's combo carries no
// customization at all (0, where both readings agree), and in OrderItemFactoryTests the only two
// tests that set CustomizationPrice are at quantity 1 (where `cust` and `cust * 1` are the same
// number) while the one test at quantity 2 leaves it at 0.
//
// The invariant every test here asserts is `sum(order.Items.ItemTotal) == basket.SubTotal` — the one
// statement that covers both directions without naming either. Note precisely what it rests on:
// BasketPricingService.ApplyTotalsAsync sums EVERY basket item, root and child alike, and equals the
// sum of the ROOT totals only because a child is pinned at ItemTotal = 0. The order side mirrors that
// pin (#54), which is why the child rows are asserted to be zero below rather than assumed.
//
// THERE IS NOW ONE PRICING PATH, and it is the row-derived one. Until S0b a caller could send
// BasketSubTotal/BasketTax/BasketTotal and TryUsePreCalculatedBasketValues would win, leaving the
// header right while the ROWS were wrong; today every money field is computed from these rows, so
// a row error IS a mischarge — there is no longer a client-supplied total papering over it. That
// makes the invariant below stricter than when it was written, not weaker.
//
// Four surfaces are row-derived and were verified to be: GetZReportQuery's TotalAmount/TotalRevenue,
// the itemsTotal CreateOrderCommandHandler feeds to the fidelity calculation, the per-line figure
// OrderMappingService puts on the order screen, and — since S0b — order.Total itself.
[Collection("Database Lane 1")]
public class OrderLineCustomizationPriceTests : IntegrationTestBase
{
    private readonly string _sessionId = Guid.NewGuid().ToString();
    private Product _testPizza = null!;
    private Product _testCola = null!;
    private Product _menuProduct = null!;
    private MenuSection _mainSection = null!;
    private MenuSection _drinkSection = null!;
    private Guid _colaExtraShotId;
    private Guid _legacyMenuId;

    private const decimal MenuBasePrice = 8.00m;
    private const decimal MainAdditional = 2.00m;
    private const decimal DrinkAdditional = 1.50m;
    private const decimal ExtraShotPrice = 1.50m;
    private const int DrinksPerBundle = 2;

    // What BuildMenuItemAsync stores on a customised bundle parent:
    //   UnitPrice = 8.00 + 2.00 + (1.50 x 2) + (1.50 x 2) = 16.00, with the last term being the
    //   customization — so CustomizationPrice = 3.00 is ALREADY inside UnitPrice, and the line total
    //   is UnitPrice * Quantity. Adding it a second time is the double-charge.
    private const decimal CustomisedBundleUnitPrice = 16.00m;

    private const int RegularQuantity = 3;
    private const int BundleQuantity = 2;

    public OrderLineCustomizationPriceTests(DatabaseFixture databaseFixture)
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

        // Priced, optional and NOT included in the base price is the one ingredient shape that is
        // charged for — the bundle needs a non-zero CustomizationPrice or its two candidate readings
        // agree and the fixture cannot see the defect.
        var extraShot = new ProductIngredient
        {
            Id = Guid.NewGuid(),
            ProductId = _testCola.Id,
            Name = "Extra Shot",
            IsOptional = true,
            IsIncludedInBasePrice = false,
            Price = ExtraShotPrice,
            MaxQuantity = 5,
            IsActive = true,
            DisplayOrder = 1,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test"
        };
        context.ProductIngredients.Add(extraShot);
        _colaExtraShotId = extraShot.Id;

        // A row in the LEGACY Menus table, seeded for one reason: the MenuId guard below needs an id
        // that actually resolves. BasketItem.MenuId is a real FK, so posting a random Guid would fail
        // on referential integrity and the guard would appear to fire for the wrong reason — and an
        // unresolvable id would also never reach AddMenuItemAsync's priced path, which is the branch
        // the guard exists to keep unreachable. RUMI prod has zero rows here, which is why the branch
        // is inert in production rather than merely unused.
        var legacyMenu = new Menu
        {
            Id = Guid.NewGuid(),
            Name = "Legacy Daily Menu",
            Date = DateOnly.FromDateTime(DateTime.UtcNow),
            BasePrice = 99.00m,   // deliberately unlike any line total here, so a leak is unmistakable
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test"
        };
        context.Menus.Add(legacyMenu);
        _legacyMenuId = legacyMenu.Id;

        var menuProduct = new Product
        {
            Id = Guid.NewGuid(),
            Name = "Order Line Combo",
            BasePrice = MenuBasePrice,
            IsActive = true,
            IsAvailable = true,
            PreparationTimeMinutes = 15,
            Type = ProductType.Menu,
            Ingredients = new List<string>(),
            Allergens = new List<string>(),
            DisplayOrder = 40,
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

        _mainSection = new MenuSection
        {
            Id = Guid.NewGuid(),
            MenuDefinitionId = menuDefinition.Id,
            Name = "Main",
            DisplayOrder = 1,
            IsRequired = true,
            MinSelection = 1,
            MaxSelection = 1,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test"
        };
        _drinkSection = new MenuSection
        {
            Id = Guid.NewGuid(),
            MenuDefinitionId = menuDefinition.Id,
            Name = "Drink",
            DisplayOrder = 2,
            IsRequired = true,
            MinSelection = 1,
            MaxSelection = DrinksPerBundle,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test"
        };

        _mainSection.Items.Add(new MenuSectionItem
        {
            Id = Guid.NewGuid(),
            MenuSectionId = _mainSection.Id,
            ProductId = _testPizza.Id,
            AdditionalPrice = MainAdditional,
            DisplayOrder = 1,
            IsDefault = true,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test"
        });
        _drinkSection.Items.Add(new MenuSectionItem
        {
            Id = Guid.NewGuid(),
            MenuSectionId = _drinkSection.Id,
            ProductId = _testCola.Id,
            AdditionalPrice = DrinkAdditional,
            DisplayOrder = 1,
            IsDefault = true,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test"
        });

        menuDefinition.Sections.Add(_mainSection);
        menuDefinition.Sections.Add(_drinkSection);
        menuProduct.MenuDefinition = menuDefinition;

        context.Products.Add(menuProduct);
        await context.SaveChangesAsync();

        _menuProduct = menuProduct;
    }

    // A regular line: UnitPrice excludes the side item, CustomizationPrice holds it PER UNIT.
    private Task<HttpResponseMessage> AddCustomisedPizzaAsync(int quantity) =>
        PostAsJsonAsync("/api/basket/items", new AddToBasketDto
        {
            ProductId = _testPizza.Id,
            Quantity = quantity,
            SelectedSideItems = new List<SelectedSideItemDto>
            {
                new() { Id = _testCola.Id, Quantity = 1 }
            }
        });

    // A bundle line: the extras are folded INTO UnitPrice, and CustomizationPrice is a display copy.
    private Task<HttpResponseMessage> AddCustomisedBundleAsync(int quantity) =>
        PostAsJsonAsync("/api/basket/items", new AddToBasketDto
        {
            ProductId = _menuProduct.Id,
            Quantity = quantity,
            SelectedMenuOptions = new List<SelectedMenuOptionDto>
            {
                new() { SectionId = _mainSection.Id, ItemId = _testPizza.Id, Quantity = 1 },
                new()
                {
                    SectionId = _drinkSection.Id,
                    ItemId = _testCola.Id,
                    Quantity = DrinksPerBundle,
                    SelectedIngredients = new List<Guid> { _colaExtraShotId }
                }
            }
        });

    private async Task<BasketDto> ReadBasketAsync()
    {
        var response = await Client.GetAsync("/api/basket");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var basket = await ReadResponseAsync<ApiResponse<BasketDto>>(response);
        return basket!.Data!;
    }

    /// <summary>
    /// Checks out the current basket. This used to take a <c>sendBasketTotals</c> flag selecting
    /// between two pricing paths — client-supplied totals vs computed. S0b removed the first, so
    /// there is one path and the flag is gone; the pairs of tests below still differ in what they
    /// assert (the stored row arithmetic vs the customer's bill), which is why both survive.
    /// </summary>
    private async Task<OrderDto> CheckoutAsync()
    {
        var request = new CreateOrderFromBasketCommand
        {
            Type = OrderType.DineIn,
            TableNumber = 7,
            CustomerName = "Test Customer",
            CustomerEmail = "test@example.com",
            CustomerPhone = "+1234567890",
        };

        var response = await PostAsJsonAsync("/api/orders/from-basket", request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await ReadResponseAsync<ApiResponse<OrderDto>>(response);
        result!.Success.Should().BeTrue();
        return result.Data!;
    }

    /// <summary>
    /// The order's ROOT rows read from the DATABASE. OrderDto.Items is root-only and nests children
    /// under SideItems, so the DTO cannot state the "children contribute 0" half; and it is the
    /// stored rows the Z-report, the fidelity calculation and the order screen all read.
    /// </summary>
    private async Task<List<OrderItem>> ReadOrderRowsAsync(Guid orderId)
    {
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var order = await context.Orders
            .Include(o => o.Items)
            .SingleAsync(o => o.Id == orderId);

        return order.Items.ToList();
    }

    // ---- The regular line: the undercharge ------------------------------------------------------

    [Fact]
    public async Task RegularLineWithASideItem_OrderRowsSumToTheBasketSubTotal()
    {
        Client.DefaultRequestHeaders.Add("X-Session-Id", _sessionId);
        (await AddCustomisedPizzaAsync(RegularQuantity)).StatusCode.Should().Be(HttpStatusCode.OK);

        var basket = await ReadBasketAsync();
        var expectedLine = (_testPizza.BasePrice + _testCola.BasePrice) * RegularQuantity;
        basket.SubTotal.Should().Be(expectedLine, "the basket side is already correct (#308)");

        var order = await CheckoutAsync();
        var rows = await ReadOrderRowsAsync(order.Id);

        var root = rows.Single(r => r.ParentOrderItemId == null);
        root.ItemTotal.Should().Be(expectedLine,
            "the side item is charged once per unit, not once per line — this row was 41.96 against a 47.94 basket");
        rows.Sum(r => r.ItemTotal).Should().Be(basket.SubTotal);
    }

    // The path with no pre-calculated totals: the same wrong rows become the customer's bill, because
    // OrderPricingService falls through to computing from itemsTotal. BasketSubTotal/Tax/Total are
    // optional by design, so any caller that omits them takes this branch.
    [Fact]
    public async Task RegularLine_WithoutPreCalculatedTotals_ChargesTheBasketsOwnFigure()
    {
        Client.DefaultRequestHeaders.Add("X-Session-Id", _sessionId);
        (await AddCustomisedPizzaAsync(RegularQuantity)).StatusCode.Should().Be(HttpStatusCode.OK);

        var basket = await ReadBasketAsync();
        var order = await CheckoutAsync();

        order.Total.Should().Be(basket.SubTotal,
            "with no basket totals supplied the order is priced from its own rows — 41.96 billed against a 47.94 basket");
        (await ReadOrderRowsAsync(order.Id)).Sum(r => r.ItemTotal).Should().Be(basket.SubTotal);
    }

    // ---- The customised bundle: the overcharge, in the opposite direction -----------------------

    [Fact]
    public async Task CustomisedBundle_DoesNotChargeTheExtrasTwice()
    {
        Client.DefaultRequestHeaders.Add("X-Session-Id", _sessionId);
        (await AddCustomisedBundleAsync(BundleQuantity)).StatusCode.Should().Be(HttpStatusCode.OK);

        var basket = await ReadBasketAsync();
        var expectedLine = CustomisedBundleUnitPrice * BundleQuantity;
        basket.SubTotal.Should().Be(expectedLine);
        basket.Items.Single().CustomizationPrice.Should().Be(ExtraShotPrice * DrinksPerBundle,
            "the fixture is only meaningful while the bundle carries a non-zero customization");

        var order = await CheckoutAsync();
        var rows = await ReadOrderRowsAsync(order.Id);

        var root = rows.Single(r => r.ParentOrderItemId == null);
        root.ItemTotal.Should().Be(expectedLine,
            "a bundle's customization is already inside UnitPrice — this row was 35.00 against a 32.00 basket");
        rows.Where(r => r.ParentOrderItemId != null).Select(r => r.ItemTotal)
            .Should().OnlyContain(t => t == 0m, "children carry zero so they cannot double-count (#54)");
        rows.Sum(r => r.ItemTotal).Should().Be(basket.SubTotal);
    }

    // The same overcharge on the unprotected pricing path, where it reaches the customer's bill
    // rather than just the rows. Measured: `CustomizationPrice * Quantity` — the obvious repair, which
    // fixes the regular line above — kills this test and the two bundle tests around it at 38.00
    // against a correct 32.00, and leaves every regular-line test passing. That asymmetry is the whole
    // reason both shapes are here.
    [Fact]
    public async Task CustomisedBundle_WithoutPreCalculatedTotals_ChargesTheBasketsOwnFigure()
    {
        Client.DefaultRequestHeaders.Add("X-Session-Id", _sessionId);
        (await AddCustomisedBundleAsync(BundleQuantity)).StatusCode.Should().Be(HttpStatusCode.OK);

        var basket = await ReadBasketAsync();
        var order = await CheckoutAsync();

        order.Total.Should().Be(CustomisedBundleUnitPrice * BundleQuantity);
        order.Total.Should().Be(basket.SubTotal);
    }

    // The derivation pairs a customization taken from the basket's UnitPrice with OrderItemFactory's
    // PRODUCT branch, which echoes that UnitPrice. Its legacy MenuId branch prices from
    // Menus.BasePrice instead, so a row that reached it would pair the two against different unit
    // prices — and worse, AddItemAsync RETURNS on that branch without recursing into ChildItems, so
    // every child row would silently vanish from the order.
    //
    // Nothing assigns BasketItem.MenuId today (BasketService's MenuId branch is an empty block whose
    // own comment invites re-enabling it, and c5180c1 did populate the column once), so the branch is
    // unreachable from the basket. That is a current-code fact, not a structural guarantee.
    //
    // THE INPUT IS THE POINT, and the first version of this test got it wrong. Driving the ordinary
    // add helpers pins only that BuildMenuItemAsync does not START setting MenuId — neither helper
    // sends the field, so the empty block is not entered whether or not someone fills it in, and the
    // test stays green through exactly the change it claims to catch. The block is gated on the
    // CLIENT'S OWN AddToBasketDto.MenuId, so the payload has to carry one for the guard to mean
    // anything.
    [Fact]
    public async Task PostingAMenuId_StillStoresNone_SoTheLegacyMenuBranchIsNeverTaken()
    {
        Client.DefaultRequestHeaders.Add("X-Session-Id", _sessionId);

        // A client-supplied MenuId alongside a real ProductId: accepted (the ProductId branch builds
        // the line) and dropped on the floor. Re-enable BasketService's empty block with the c5180c1
        // body and this row comes back carrying the id, which is what fails the assertion below.
        (await PostAsJsonAsync("/api/basket/items", new AddToBasketDto
        {
            ProductId = _testPizza.Id,
            MenuId = _legacyMenuId,
            Quantity = RegularQuantity,
            SelectedSideItems = new List<SelectedSideItemDto>
            {
                new() { Id = _testCola.Id, Quantity = 1 }
            }
        })).StatusCode.Should().Be(HttpStatusCode.OK);

        using (var scope = Factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var stored = await context.BasketItems
                .Where(bi => bi.Basket!.SessionId == _sessionId)
                .ToListAsync();

            stored.Should().NotBeEmpty("the line must exist for its MenuId to be worth asserting");
            stored.Should().OnlyContain(bi => bi.MenuId == null,
                "the client's MenuId is discarded — nothing persists it, which is what keeps the legacy order branch unreachable");
        }

        var basket = await ReadBasketAsync();
        basket.Items.Should().OnlyContain(i => i.MenuId == null,
            "so the translator has nothing to copy into CreateOrderItemDto.MenuId");

        var order = await CheckoutAsync();
        var rows = await ReadOrderRowsAsync(order.Id);

        rows.Should().OnlyContain(r => r.MenuId == null);
        // The child row is the observable consequence: AddItemAsync returns on the menu branch without
        // recursing into ChildItems, so a line that reached it would lose its side item entirely.
        rows.Where(r => r.ParentOrderItemId != null).Should().HaveCount(
            1, "the pizza's side item, reached only by the product branch's recursion");
    }

    // ---- Both shapes at once, and the case that cannot see the defect ---------------------------

    [Fact]
    public async Task BothShapesInOneBasket_EveryRootRowEqualsItsBasketLine()
    {
        Client.DefaultRequestHeaders.Add("X-Session-Id", _sessionId);
        (await AddCustomisedPizzaAsync(RegularQuantity)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await AddCustomisedBundleAsync(BundleQuantity)).StatusCode.Should().Be(HttpStatusCode.OK);

        var basket = await ReadBasketAsync();
        var order = await CheckoutAsync();
        var rows = await ReadOrderRowsAsync(order.Id);

        // Root rows keyed by product, against the basket's own lines. Asserted per line rather than
        // only on the sum: the two errors point in opposite directions, so a total can reconcile
        // while both of its terms are wrong.
        foreach (var basketLine in basket.Items)
        {
            rows.Single(r => r.ParentOrderItemId == null && r.ProductId == basketLine.ProductId)
                .ItemTotal.Should().Be(basketLine.ItemTotal,
                    $"the order row for {basketLine.ProductName} must equal its basket line");
        }

        rows.Sum(r => r.ItemTotal).Should().Be(basket.SubTotal);
    }

    // An uncustomised line of each shape must be untouched. CustomizationPrice is 0 there, so the
    // copy and the derivation agree — which is exactly why the pre-#312 suites were all green, and
    // why it is worth pinning that the fix does not disturb the case they did cover.
    [Fact]
    public async Task UncustomisedLines_AreUnaffected()
    {
        Client.DefaultRequestHeaders.Add("X-Session-Id", _sessionId);

        (await PostAsJsonAsync("/api/basket/items",
            new AddToBasketDto { ProductId = _testPizza.Id, Quantity = RegularQuantity }))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await PostAsJsonAsync("/api/basket/items", new AddToBasketDto
        {
            ProductId = _menuProduct.Id,
            Quantity = BundleQuantity,
            SelectedMenuOptions = new List<SelectedMenuOptionDto>
            {
                new() { SectionId = _mainSection.Id, ItemId = _testPizza.Id, Quantity = 1 },
                new() { SectionId = _drinkSection.Id, ItemId = _testCola.Id, Quantity = 1 }
            }
        })).StatusCode.Should().Be(HttpStatusCode.OK);

        var basket = await ReadBasketAsync();
        basket.Items.Should().OnlyContain(i => i.CustomizationPrice == 0m);

        var order = await CheckoutAsync();
        var rows = await ReadOrderRowsAsync(order.Id);

        rows.Single(r => r.ParentOrderItemId == null && r.ProductId == _testPizza.Id)
            .ItemTotal.Should().Be(_testPizza.BasePrice * RegularQuantity);
        rows.Single(r => r.ParentOrderItemId == null && r.ProductId == _menuProduct.Id)
            .ItemTotal.Should().Be((MenuBasePrice + MainAdditional + DrinkAdditional) * BundleQuantity);
        rows.Sum(r => r.ItemTotal).Should().Be(basket.SubTotal);
        order.Total.Should().Be(basket.SubTotal);
    }
}
