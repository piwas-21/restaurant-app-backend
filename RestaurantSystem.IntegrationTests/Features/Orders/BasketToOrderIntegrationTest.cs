using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Basket.Dtos;
using RestaurantSystem.Api.Features.Basket.Dtos.Requests;
using RestaurantSystem.Api.Features.Orders.Commands.CreateOrderCommand;
using RestaurantSystem.Api.Features.Orders.Commands.CreateOrderFromBasketCommand;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Infrastructure;
using System.Net;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

public class BasketToOrderIntegrationTest : IntegrationTestBase
{
    private readonly string _sessionId = Guid.NewGuid().ToString();
    private Product _testProduct = null!;
    private Product _testCola = null!;
    // ProductType.Menu product — the production model replacing the legacy
    // standalone Menu entity. Holds a MenuDefinition + Sections + Items
    // referencing other Products as the selectable options.
    private Product _menuProduct = null!;
    private MenuSection _mainSection = null!;
    private MenuSection _drinkSection = null!;
    private MenuSectionItem _pizzaOption = null!;
    private MenuSectionItem _colaOption = null!;

    // Per-section additional prices on top of the menu product's BasePrice.
    // BasePrice (8) + main additional (2.99) + drink additional (1.99) = 12.98
    private const decimal MenuBasePrice = 8.00m;
    private const decimal MainAdditional = 2.99m;
    private const decimal DrinkAdditional = 1.99m;
    private const decimal ExpectedMenuUnitPrice = MenuBasePrice + MainAdditional + DrinkAdditional;

    public BasketToOrderIntegrationTest(DatabaseFixture databaseFixture)
        : base(databaseFixture)
    {
    }

    protected override async Task SeedTestData()
    {
        await base.SeedTestData();

        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        _testProduct = await context.Products
            .FirstAsync(p => p.Name == "Test Pizza");
        _testCola = await context.Products
            .FirstAsync(p => p.Name == "Test Cola");

        // Seed a Product with Type == Menu, plus its MenuDefinition tree.
        // This is the live shape exercised by BasketService when adding a
        // menu to the basket (see the ProductType.Menu branch in
        // BasketService.AddItemToBasketAsync). The legacy standalone Menu
        // table is no longer the source of truth.
        var menuProduct = new Product
        {
            Id = Guid.NewGuid(),
            Name = "Lunch Special Combo",
            Description = "Pick a main + a drink",
            BasePrice = MenuBasePrice,
            IsActive = true,
            IsAvailable = true,
            PreparationTimeMinutes = 20,
            Type = ProductType.Menu,
            Ingredients = new List<string>(),
            Allergens = new List<string>(),
            DisplayOrder = 10,
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

        var mainSection = new MenuSection
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

        var drinkSection = new MenuSection
        {
            Id = Guid.NewGuid(),
            MenuDefinitionId = menuDefinition.Id,
            Name = "Drink",
            DisplayOrder = 2,
            IsRequired = true,
            MinSelection = 1,
            MaxSelection = 1,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test"
        };

        var pizzaOption = new MenuSectionItem
        {
            Id = Guid.NewGuid(),
            MenuSectionId = mainSection.Id,
            ProductId = _testProduct.Id,
            AdditionalPrice = MainAdditional,
            DisplayOrder = 1,
            IsDefault = true,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test"
        };

        var colaOption = new MenuSectionItem
        {
            Id = Guid.NewGuid(),
            MenuSectionId = drinkSection.Id,
            ProductId = _testCola.Id,
            AdditionalPrice = DrinkAdditional,
            DisplayOrder = 1,
            IsDefault = true,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test"
        };

        mainSection.Items.Add(pizzaOption);
        drinkSection.Items.Add(colaOption);
        menuDefinition.Sections.Add(mainSection);
        menuDefinition.Sections.Add(drinkSection);
        menuProduct.MenuDefinition = menuDefinition;

        context.Products.Add(menuProduct);
        await context.SaveChangesAsync();

        _menuProduct = menuProduct;
        _mainSection = mainSection;
        _drinkSection = drinkSection;
        _pizzaOption = pizzaOption;
        _colaOption = colaOption;
    }

    // Shared basket-building helpers so the legacy-path, totals, and from-basket parity tests
    // add identical items without duplicating the request payloads.
    private Task<HttpResponseMessage> AddStandalonePizzaAsync(int quantity, string? instructions = null) =>
        PostAsJsonAsync("/api/basket/items", new AddToBasketDto
        {
            ProductId = _testProduct.Id,
            Quantity = quantity,
            SpecialInstructions = instructions,
        });

    private Task<HttpResponseMessage> AddComboAsync(int quantity, string? instructions = null) =>
        PostAsJsonAsync("/api/basket/items", new AddToBasketDto
        {
            ProductId = _menuProduct.Id,
            Quantity = quantity,
            SpecialInstructions = instructions,
            SelectedMenuOptions = new List<SelectedMenuOptionDto>
            {
                new() { SectionId = _mainSection.Id, ItemId = _pizzaOption.ProductId, Quantity = 1 },
                new() { SectionId = _drinkSection.Id, ItemId = _colaOption.ProductId, Quantity = 1 }
            }
        });

    [Fact]
    public async Task Should_Add_Product_And_Menu_To_Basket_Then_Create_Order_Successfully()
    {
        // Arrange - Work in anonymous mode with session ID only
        // Don't authenticate to avoid user ID foreign key issues
        Client.DefaultRequestHeaders.Add("X-Session-Id", _sessionId);

        // Act & Assert - Step 1: Add Product to Basket
        var productResponse = await AddStandalonePizzaAsync(2, "Extra cheese please");
        productResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var basketAfterProduct = await ReadResponseAsync<ApiResponse<BasketDto>>(productResponse);
        basketAfterProduct.Should().NotBeNull();
        basketAfterProduct!.Success.Should().BeTrue();
        basketAfterProduct.Data.Should().NotBeNull();
        basketAfterProduct.Data!.Items.Should().HaveCount(1);
        basketAfterProduct.Data.Items.First().ProductId.Should().Be(_testProduct.Id);
        basketAfterProduct.Data.Items.First().Quantity.Should().Be(2);

        // Act & Assert - Step 2: Add Menu (ProductType.Menu product) to Basket
        var menuResponse = await AddComboAsync(1, "No ice in drink");
        menuResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var basketAfterMenu = await ReadResponseAsync<ApiResponse<BasketDto>>(menuResponse);
        basketAfterMenu.Should().NotBeNull();
        basketAfterMenu!.Success.Should().BeTrue();
        basketAfterMenu.Data.Should().NotBeNull();
        // Top-level items: standalone pizza + the combo parent.
        // Child basket items (the selected options) hang off the combo via
        // ParentBasketItemId and are not surfaced at the top level.
        basketAfterMenu.Data!.Items.Should().HaveCount(2);

        // Verify both items are in basket. The standalone pizza has no
        // child items; the combo parent has two (pizza + cola options).
        var productItem = basketAfterMenu.Data.Items
            .FirstOrDefault(i => i.ProductId == _testProduct.Id
                && (i.ChildItems == null || i.ChildItems.Count == 0));
        var menuItem = basketAfterMenu.Data.Items
            .FirstOrDefault(i => i.ProductId == _menuProduct.Id);

        productItem.Should().NotBeNull();
        productItem!.Quantity.Should().Be(2);
        menuItem.Should().NotBeNull();
        menuItem!.UnitPrice.Should().Be(ExpectedMenuUnitPrice);
        menuItem.ChildItems.Should().NotBeNull();
        menuItem.ChildItems!.Should().HaveCount(2);

        // Act & Assert - Step 3: Get Basket Summary
        var summaryResponse = await Client.GetAsync("/api/basket/summary");
        summaryResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var summary = await ReadResponseAsync<ApiResponse<BasketSummaryDto>>(summaryResponse);
        summary.Should().NotBeNull();
        summary!.Success.Should().BeTrue();
        summary.Data.Should().NotBeNull();
        // ItemCount sums quantity across top-level items: 2 pizzas + 1 menu = 3.
        summary.Data!.ItemCount.Should().Be(3);
        summary.Data.Total.Should().BeGreaterThan(0);

        // Act & Assert - Step 4: Create Order from Basket
        // Authenticate for order creation as it requires authentication
        AuthenticateAsTestUser();

        var createOrderRequest = new CreateOrderCommand
        {
            Type = OrderType.DineIn,
            TableNumber = 5,
            CustomerName = "Test Customer",
            CustomerEmail = "test@example.com",
            CustomerPhone = "+1234567890",
            Notes = "Please prepare quickly",
            Items = new List<CreateOrderItemDto>
            {
                new()
                {
                    ProductId = _testProduct.Id,
                    Quantity = 2,
                    UnitPrice = _testProduct.BasePrice,
                    SpecialInstructions = "Extra cheese please"
                },
                new()
                {
                    ProductId = _menuProduct.Id,
                    Quantity = 1,
                    UnitPrice = ExpectedMenuUnitPrice,
                    SpecialInstructions = "No ice in drink",
                    // Realistic shape: the frontend converts basket → order
                    // DTO and carries each child's per-section additional
                    // price as UnitPrice (matches BasketService's basket-side
                    // storage). The assertion below pins the resulting items
                    // total — see the note next to it about the latent
                    // OrderItemFactory double-count behavior.
                    ChildItems = new List<CreateOrderItemDto>
                    {
                        new()
                        {
                            ProductId = _pizzaOption.ProductId,
                            Quantity = 1,
                            UnitPrice = MainAdditional
                        },
                        new()
                        {
                            ProductId = _colaOption.ProductId,
                            Quantity = 1,
                            UnitPrice = DrinkAdditional
                        }
                    }
                }
            },
            Payments = new List<CreateOrderPaymentDto>
            {
                new()
                {
                    PaymentMethod = PaymentMethod.Cash,
                    Amount = 100.00m
                }
            }
        };

        var orderResponse = await PostAsJsonAsync("/api/orders", createOrderRequest);
        orderResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var orderResult = await ReadResponseAsync<ApiResponse<OrderDto>>(orderResponse);
        orderResult.Should().NotBeNull();
        orderResult!.Success.Should().BeTrue();
        orderResult.Data.Should().NotBeNull();

        // Verify Order Details
        var createdOrder = orderResult.Data!;
        createdOrder.OrderNumber.Should().NotBeNullOrEmpty();
        createdOrder.Type.Should().Be(OrderType.DineIn.ToString());
        createdOrder.TableNumber.Should().Be(5);
        createdOrder.CustomerName.Should().Be("Test Customer");
        createdOrder.Status.Should().Be(OrderStatus.Confirmed.ToString());
        createdOrder.PaymentStatus.Should().Be(PaymentStatus.Pending.ToString());
        // OrderDto.Items holds only ROOT rows; child rows hang off their parent's
        // SideItems (#234 — the flat projection emitted every child twice on the
        // paths where SideItems was populated, and left it null on the printer
        // feed). Two roots here: the standalone pizza and the combo parent. The
        // combo's two child option rows (pizza + cola) are asserted below.
        createdOrder.Items.Should().HaveCount(2);

        // Verify Order Items - standalone pizza (qty 2, no parent)
        var orderProductItem = createdOrder.Items
            .FirstOrDefault(i => i.ProductId == _testProduct.Id && i.Quantity == 2);
        orderProductItem.Should().NotBeNull();
        orderProductItem!.ProductName.Should().Be("Test Pizza");

        // Verify Order Items - menu (ProductType.Menu) parent
        var orderMenuItem = createdOrder.Items
            .FirstOrDefault(i => i.ProductId == _menuProduct.Id);
        orderMenuItem.Should().NotBeNull();
        orderMenuItem!.Quantity.Should().Be(1);
        orderMenuItem.ProductName.Should().Be("Lunch Special Combo");

        // The combo's children are nested under it, not listed top-level. The combo
        // parent is ProductType.Menu, so its children are stamped BundleChild.
        createdOrder.Items.Should().NotContain(i => i.ProductId == _testCola.Id,
            "child rows belong under their parent's SideItems, not at the top level");
        orderMenuItem.SideItems.Should().NotBeNull().And.HaveCount(2);
        var orderColaChild = orderMenuItem.SideItems!
            .FirstOrDefault(i => i.ProductId == _testCola.Id);
        orderColaChild.Should().NotBeNull();
        orderColaChild!.ProductName.Should().Be("Test Cola");
        orderColaChild.Kind.Should().Be(OrderItemKind.BundleChild);

        // Verify Order Totals
        //
        // itemsTotal sums ItemTotal across the root rows. Children carry ItemTotal = 0
        // (see below), so root-only summing is equivalent to the old flat sum. Pinning
        // the exact value so this assertion fails loudly if pricing logic ever shifts.
        //
        // Per issue #54 (now fixed): OrderItemFactory aligns with
        // BasketService.AddItemToBasketAsync (BasketService.cs:230-231) —
        // child OrderItem rows carry UnitPrice for display but
        // ItemTotal = 0, because the parent menu OrderItem's ItemTotal
        // already includes the full combo price (ExpectedMenuUnitPrice
        // rolls up MainAdditional + DrinkAdditional via the frontend's
        // basket-to-order mapping). Summing children's UnitPrice on top
        // would double-count those terms — only the standalone pizza and
        // the menu parent contribute.
        var expectedItemsTotal =
            (_testProduct.BasePrice * 2) // standalone pizza (qty 2)
            + ExpectedMenuUnitPrice;     // menu parent (children contribute 0)
        createdOrder.Items.Sum(i => i.ItemTotal).Should().Be(expectedItemsTotal);
        createdOrder.SubTotal.Should().BeGreaterThan(0);
        createdOrder.SubTotal.Should().BeLessOrEqualTo(expectedItemsTotal);
        createdOrder.Total.Should().BeGreaterThan(0);
        createdOrder.Payments.Should().HaveCount(1);
        createdOrder.Payments.First().PaymentMethod.Should().Be(PaymentMethod.Cash.ToString());

        // Act & Assert - Step 5: Verify Order in Database
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var orderInDb = await context.Orders
            .Include(o => o.Items)
            .Include(o => o.Payments)
            .FirstOrDefaultAsync(o => o.Id == createdOrder.Id);

        orderInDb.Should().NotBeNull();
        // Top-level + child OrderItems are stored flat; both rows persist.
        orderInDb!.Items.Should().HaveCount(4);
        orderInDb.Payments.Should().HaveCount(1);
        orderInDb.OrderNumber.Should().Be(createdOrder.OrderNumber);

        // Issue #54 acceptance: every row with ParentOrderItemId != null
        // must have ItemTotal == 0, mirroring BasketService's child-zero
        // convention. Asserted against the DB row (the DTO doesn't carry
        // ParentOrderItemId, and ProductId alone isn't a reliable
        // discriminator since menu-option children can reference the same
        // Product as a standalone item — pizzaOption.ProductId ==
        // _testProduct.Id in this fixture).
        orderInDb.Items
            .Where(i => i.ParentOrderItemId != null)
            .Should().HaveCount(2);
        orderInDb.Items
            .Where(i => i.ParentOrderItemId != null)
            .Select(i => i.ItemTotal)
            .Should().OnlyContain(t => t == 0m);
        // Parent menu row carries the full combo ItemTotal; children
        // carry UnitPrice (for display) but contribute 0 to the sum.
        orderInDb.Items
            .Where(i => i.ParentOrderItemId != null)
            .Select(i => i.UnitPrice)
            .Should().BeEquivalentTo(new[] { MainAdditional, DrinkAdditional });
    }

    // FluentValidation now runs in the CustomMediator pipeline via
    // ValidationBehavior<TRequest, TResponse>. Empty-items orders are
    // rejected by CreateOrderCommandValidator → BadRequestException → 400.
    [Fact]
    public async Task Should_Handle_Empty_Basket_When_Creating_Order()
    {
        // Arrange - Work in anonymous mode
        var emptySessionId = Guid.NewGuid().ToString();
        Client.DefaultRequestHeaders.Add("X-Session-Id", emptySessionId);

        // Act - Try to create order with no items
        var createOrderRequest = new CreateOrderCommand
        {
            Type = OrderType.DineIn,
            TableNumber = 3,
            CustomerName = "Test Customer",
            Items = new List<CreateOrderItemDto>(), // Empty items
            Payments = new List<CreateOrderPaymentDto>
            {
                new CreateOrderPaymentDto
                {
                    PaymentMethod = PaymentMethod.Cash,
                    Amount = 10.00m
                }
            }
        };

        var response = await PostAsJsonAsync("/api/orders", createOrderRequest);

        // Assert - Validation behavior rejects empty orders with HTTP 400.
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Should_Calculate_Correct_Totals_With_Multiple_Items()
    {
        // Arrange - Work in anonymous mode
        Client.DefaultRequestHeaders.Add("X-Session-Id", _sessionId);

        // Add multiple products to basket
        await AddStandalonePizzaAsync(3);

        // Add a ProductType.Menu combo with its required selections.
        await AddComboAsync(2);

        // Get basket to verify totals
        var basketResponse = await Client.GetAsync("/api/basket");
        basketResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var basket = await ReadResponseAsync<ApiResponse<BasketDto>>(basketResponse);
        basket!.Data.Should().NotBeNull();

        // Calculate expected totals
        var expectedProductTotal = _testProduct.BasePrice * 3;
        var expectedMenuTotal = ExpectedMenuUnitPrice * 2;
        var expectedSubTotal = expectedProductTotal + expectedMenuTotal;

        basket.Data!.SubTotal.Should().Be(expectedSubTotal);
        basket.Data.Total.Should().BeGreaterThanOrEqualTo(expectedSubTotal); // May include tax/fees
    }

    // Slice 5 (#157): POST /api/orders/from-basket reads the persisted basket and the SERVER owns
    // the basket→order item translation (replacing the client's orderItemsPayload.ts). This pins
    // parity with the legacy items-payload path — same basket ⇒ identical order rows + totals —
    // which is what makes deleting the client-side transform safe. Runs as a guest checkout
    // (anonymous throughout) so GetBasketAsync resolves the session-owned basket; item translation
    // (the thing under test) is independent of authentication.
    [Fact]
    public async Task Should_Create_Order_From_Basket_With_Same_Rows_And_Totals_As_Legacy_Path()
    {
        var sessionId = Guid.NewGuid().ToString();
        Client.DefaultRequestHeaders.Add("X-Session-Id", sessionId);

        (await AddStandalonePizzaAsync(2, "Extra cheese please")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await AddComboAsync(1, "No ice in drink")).StatusCode.Should().Be(HttpStatusCode.OK);

        // Act — no client-built Items; the server derives them from the basket.
        var request = new CreateOrderFromBasketCommand
        {
            Type = OrderType.DineIn,
            TableNumber = 5,
            CustomerName = "Test Customer",
            CustomerEmail = "test@example.com",
            CustomerPhone = "+1234567890",
            Notes = "Please prepare quickly",
            Payments = new List<CreateOrderPaymentDto>
            {
                new() { PaymentMethod = PaymentMethod.Cash, Amount = 100.00m }
            }
        };

        var orderResponse = await PostAsJsonAsync("/api/orders/from-basket", request);
        orderResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var orderResult = await ReadResponseAsync<ApiResponse<OrderDto>>(orderResponse);
        orderResult!.Success.Should().BeTrue();
        var order = orderResult.Data!;

        // Order-level fields carried through.
        order.Type.Should().Be(OrderType.DineIn.ToString());
        order.TableNumber.Should().Be(5);
        order.CustomerName.Should().Be("Test Customer");
        order.Status.Should().Be(OrderStatus.Confirmed.ToString());

        // Same rows as the legacy path, in the same shape: 2 roots (standalone pizza +
        // combo parent) with the 2 combo children nested under the parent (#234). DB-level
        // row parity is asserted separately below.
        order.Items.Should().HaveCount(2);
        order.Items.FirstOrDefault(i => i.ProductId == _testProduct.Id && i.Quantity == 2)
            .Should().NotBeNull("the standalone pizza (qty 2) must be present");
        var comboParent = order.Items.FirstOrDefault(i => i.ProductId == _menuProduct.Id);
        comboParent.Should().NotBeNull("the combo parent must be present");
        comboParent!.SideItems.Should().NotBeNull().And.HaveCount(2);
        comboParent.SideItems!.FirstOrDefault(i => i.ProductId == _testCola.Id)
            .Should().NotBeNull("the cola combo child must be nested under the combo parent");

        // Identical items total: children contribute 0 (their price is rolled into the parent's
        // UnitPrice by BasketService), so only the standalone pizza + the combo parent count.
        var expectedItemsTotal = (_testProduct.BasePrice * 2) + ExpectedMenuUnitPrice;
        order.Items.Sum(i => i.ItemTotal).Should().Be(expectedItemsTotal);

        // DB-level parity: combo children persist with ItemTotal 0 and their per-section UnitPrice
        // (issue #54), and the combo's special instructions round-tripped basket→order.
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var orderInDb = await context.Orders
            .Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Id == order.Id);

        orderInDb!.Items.Should().HaveCount(4);
        orderInDb.Items.Where(i => i.ParentOrderItemId != null).Should().HaveCount(2);
        orderInDb.Items
            .Where(i => i.ParentOrderItemId != null)
            .Select(i => i.ItemTotal)
            .Should().OnlyContain(t => t == 0m);
        orderInDb.Items
            .Where(i => i.ParentOrderItemId != null)
            .Select(i => i.UnitPrice)
            .Should().BeEquivalentTo(new[] { MainAdditional, DrinkAdditional });
        orderInDb.Items
            .First(i => i.ProductId == _menuProduct.Id && i.ParentOrderItemId == null)
            .SpecialInstructions.Should().Be("No ice in drink");
    }

    // An empty (or missing) basket is rejected with HTTP 400 — the same contract the legacy
    // items-payload path gets from CreateOrderCommandValidator's non-empty-items rule.
    [Fact]
    public async Task Should_Reject_From_Basket_Order_When_Basket_Is_Empty()
    {
        var emptySessionId = Guid.NewGuid().ToString();
        Client.DefaultRequestHeaders.Add("X-Session-Id", emptySessionId);

        var request = new CreateOrderFromBasketCommand
        {
            Type = OrderType.DineIn,
            TableNumber = 3,
            CustomerName = "Test Customer",
            Payments = new List<CreateOrderPaymentDto>
            {
                new() { PaymentMethod = PaymentMethod.Cash, Amount = 10.00m }
            }
        };

        var response = await PostAsJsonAsync("/api/orders/from-basket", request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
