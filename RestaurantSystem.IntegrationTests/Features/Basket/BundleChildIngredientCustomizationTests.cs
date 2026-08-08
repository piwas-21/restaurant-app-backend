using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Basket.Dtos;
using RestaurantSystem.Api.Features.Basket.Dtos.Requests;
using RestaurantSystem.Api.Features.Orders.Commands.CreateOrderCommand;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Infrastructure;
using System.Net;
using System.Text;

namespace RestaurantSystem.IntegrationTests.Features.Basket;

// Issue #150: bundle child ingredient customizations never reached the order /
// kitchen ticket. These tests pin the fixed pipeline end to end:
//  - BasketItemFactory backfills quantity = 0 for deselected optional
//    ingredients on bundle children (the "NO xxx" kitchen mechanism that
//    regular items already had),
//  - BasketMappingService round-trips the child's SelectedIngredients /
//    IngredientQuantities / SpecialInstructions through the cart DTO,
//  - OrderItemFactory persists child IngredientQuantities and
//    OrderMappingService derives IsRemoved for child order items.
// Issue #151 (redesign slice 1): the per-option SelectedSideItems field was removed
// from SelectedMenuOptionDto (bundle-child sides were never persisted or displayed);
// the last test pins that a stale client still sending it is tolerated and ignored.
public class BundleChildIngredientCustomizationTests : IntegrationTestBase
{
    private readonly string _sessionId = Guid.NewGuid().ToString();
    private Product _testPizza = null!;
    private Product _testCola = null!;
    private Product _menuProduct = null!;
    private MenuSection _mainSection = null!;
    private MenuSection _drinkSection = null!;
    private ProductIngredient _cheese = null!;     // optional, included in base price
    private ProductIngredient _mushrooms = null!;  // optional extra, priced
    private ProductIngredient _tomatoSauce = null!; // required (non-optional)

    private const decimal MenuBasePrice = 8.00m;
    private const decimal MainAdditional = 2.99m;
    private const decimal DrinkAdditional = 1.99m;
    private const decimal ExpectedMenuUnitPrice = MenuBasePrice + MainAdditional + DrinkAdditional;

    public BundleChildIngredientCustomizationTests(DatabaseFixture databaseFixture)
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

        // Detailed ingredients on the pizza — the product selected as a bundle child.
        _cheese = new ProductIngredient
        {
            Id = Guid.NewGuid(),
            ProductId = _testPizza.Id,
            Name = "Cheese",
            IsOptional = true,
            IsIncludedInBasePrice = true,
            Price = 1.00m,
            MaxQuantity = 2,
            IsActive = true,
            DisplayOrder = 1,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test"
        };
        _mushrooms = new ProductIngredient
        {
            Id = Guid.NewGuid(),
            ProductId = _testPizza.Id,
            Name = "Mushrooms",
            IsOptional = true,
            IsIncludedInBasePrice = false,
            Price = 2.00m,
            MaxQuantity = 3,
            IsActive = true,
            DisplayOrder = 2,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test"
        };
        _tomatoSauce = new ProductIngredient
        {
            Id = Guid.NewGuid(),
            ProductId = _testPizza.Id,
            Name = "Tomato Sauce",
            IsOptional = false,
            IsIncludedInBasePrice = false,
            Price = 0m,
            MaxQuantity = 1,
            IsActive = true,
            DisplayOrder = 3,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test"
        };
        context.ProductIngredients.AddRange(_cheese, _mushrooms, _tomatoSauce);

        // ProductType.Menu combo: pick a main (pizza) + a drink (cola).
        // Same live shape as BasketToOrderIntegrationTest.
        var menuProduct = new Product
        {
            Id = Guid.NewGuid(),
            Name = "Customizable Combo",
            Description = "Pick a main + a drink",
            BasePrice = MenuBasePrice,
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
            MaxSelection = 1,
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

    private Task<HttpResponseMessage> AddMenuToBasketAsync(SelectedMenuOptionDto mainOption)
    {
        var request = new AddToBasketDto
        {
            ProductId = _menuProduct.Id,
            Quantity = 1,
            SelectedMenuOptions = new List<SelectedMenuOptionDto>
            {
                mainOption,
                new() { SectionId = _drinkSection.Id, ItemId = _testCola.Id, Quantity = 1 }
            }
        };
        return PostAsJsonAsync("/api/basket/items", request);
    }

    private static BasketItemDto GetChildItem(BasketDto basket, Guid parentProductId, Guid childProductId)
    {
        var parent = basket.Items.FirstOrDefault(i => i.ProductId == parentProductId);
        parent.Should().NotBeNull();
        parent!.ChildItems.Should().NotBeNull();
        var child = parent.ChildItems!.FirstOrDefault(c => c.ProductId == childProductId);
        child.Should().NotBeNull();
        return child!;
    }

    [Fact]
    public async Task BundleChild_DeselectedOptionalIngredient_IsBackfilledWithQuantityZero()
    {
        Client.DefaultRequestHeaders.Add("X-Session-Id", _sessionId);

        // Cheese deselected: absent from BOTH the selection list and the quantities
        // map (the frontend deletes deselected optionals rather than zeroing them).
        var response = await AddMenuToBasketAsync(new SelectedMenuOptionDto
        {
            SectionId = _mainSection.Id,
            ItemId = _testPizza.Id,
            Quantity = 1,
            SelectedIngredients = new List<Guid> { _tomatoSauce.Id, _mushrooms.Id },
            IngredientQuantities = new Dictionary<Guid, int> { [_mushrooms.Id] = 1 }
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var basket = await ReadResponseAsync<ApiResponse<BasketDto>>(response);
        var pizzaChild = GetChildItem(basket!.Data!, _menuProduct.Id, _testPizza.Id);

        pizzaChild.SelectedIngredients.Should().BeEquivalentTo(
            new[] { _tomatoSauce.Id, _mushrooms.Id });
        pizzaChild.IngredientQuantities.Should().NotBeNull();
        // The deselected optional gets an explicit quantity-0 entry — this is what
        // lets the kitchen ticket print "NO Cheese" for a bundle child.
        pizzaChild.IngredientQuantities![_cheese.Id].Should().Be(0);
        pizzaChild.IngredientQuantities[_mushrooms.Id].Should().Be(1);
        pizzaChild.IngredientQuantities[_tomatoSauce.Id].Should().Be(1);

        // The persisted child row carries the same backfilled map.
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var childRow = await context.BasketItems
            .SingleAsync(bi => bi.Id == pizzaChild.Id);
        childRow.IngredientQuantitiesJson.Should().NotBeNullOrEmpty();
        childRow.IngredientQuantitiesJson.Should().Contain($"\"{_cheese.Id}\":0");

        // The uncustomized cola child stays untouched — no backfill without a selection.
        var colaChild = GetChildItem(basket.Data!, _menuProduct.Id, _testCola.Id);
        colaChild.IngredientQuantities.Should().BeNull();
        colaChild.SelectedIngredients.Should().BeNull();
    }

    [Fact]
    public async Task BundleChild_ExtraQuantityAndSpecialInstructions_AreCarriedThrough()
    {
        Client.DefaultRequestHeaders.Add("X-Session-Id", _sessionId);

        var response = await AddMenuToBasketAsync(new SelectedMenuOptionDto
        {
            SectionId = _mainSection.Id,
            ItemId = _testPizza.Id,
            Quantity = 1,
            SpecialInstructions = "Extra crispy",
            SelectedIngredients = new List<Guid> { _tomatoSauce.Id, _cheese.Id, _mushrooms.Id },
            IngredientQuantities = new Dictionary<Guid, int> { [_mushrooms.Id] = 2 }
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var basket = await ReadResponseAsync<ApiResponse<BasketDto>>(response);
        var pizzaChild = GetChildItem(basket!.Data!, _menuProduct.Id, _testPizza.Id);

        pizzaChild.SpecialInstructions.Should().Be("Extra crispy");
        pizzaChild.IngredientQuantities.Should().NotBeNull();
        pizzaChild.IngredientQuantities![_mushrooms.Id].Should().Be(2); // explicit extra wins
        pizzaChild.IngredientQuantities[_cheese.Id].Should().Be(1);     // selected default
        pizzaChild.IngredientQuantities[_tomatoSauce.Id].Should().Be(1);
        // Two extra mushrooms at 2.00 each.
        pizzaChild.CustomizationPrice.Should().Be(2 * _mushrooms.Price);
    }

    [Fact]
    public async Task CreateOrder_ChildItemIngredientQuantities_PersistAndDeriveIsRemoved()
    {
        // Staff, not a customer. S0b stopped an untrusted caller hand-building a composed line at
        // all (its price lives in the menu definition, so the catalogue cannot reprice it), which
        // makes this payload a TILL payload now. What it still pins — child ingredient quantities
        // round-tripping into IsRemoved — is identical on both paths; the customer equivalent goes
        // through /from-basket and is covered by BasketToOrderIntegrationTest.
        AuthenticateAsAdmin();

        // The checkout payload the (fixed) frontend builds from the basket child:
        // cheese explicitly 0 (deselected), mushrooms doubled, sauce untouched.
        var createOrderRequest = new CreateOrderCommand
        {
            Type = OrderType.DineIn,
            TableNumber = 7,
            CustomerName = "Bundle Customer",
            Items = new List<CreateOrderItemDto>
            {
                new()
                {
                    ProductId = _menuProduct.Id,
                    Quantity = 1,
                    UnitPrice = ExpectedMenuUnitPrice,
                    ChildItems = new List<CreateOrderItemDto>
                    {
                        new()
                        {
                            ProductId = _testPizza.Id,
                            Quantity = 1,
                            UnitPrice = MainAdditional,
                            IngredientQuantities = new Dictionary<Guid, int>
                            {
                                [_cheese.Id] = 0,
                                [_mushrooms.Id] = 2,
                                [_tomatoSauce.Id] = 1
                            }
                        },
                        new()
                        {
                            ProductId = _testCola.Id,
                            Quantity = 1,
                            UnitPrice = DrinkAdditional
                        }
                    }
                }
            },
            Payments = new List<CreateOrderPaymentDto>
            {
                new() { PaymentMethod = PaymentMethod.Cash, Amount = 20.00m }
            }
        };

        var response = await PostAsJsonAsync("/api/orders", createOrderRequest);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var orderResult = await ReadResponseAsync<ApiResponse<OrderDto>>(response);
        orderResult!.Success.Should().BeTrue();
        var createdOrder = orderResult.Data!;

        // OrderDto.Items holds only root rows; the pizza exists solely as the combo's
        // child, so it is reached through the parent's SideItems (#234).
        var comboParent = createdOrder.Items.Single();
        comboParent.ProductId.Should().Be(_menuProduct.Id);
        var pizzaOrderItem = comboParent.SideItems?.FirstOrDefault(i => i.ProductId == _testPizza.Id);
        pizzaOrderItem.Should().NotBeNull();
        pizzaOrderItem!.IngredientCustomizations.Should().NotBeNull();

        var cheese = pizzaOrderItem.IngredientCustomizations!
            .Single(c => c.IngredientId == _cheese.Id);
        cheese.IsRemoved.Should().BeTrue();  // quantity 0 → "NO Cheese" on the ticket
        cheese.Quantity.Should().Be(0);

        var mushrooms = pizzaOrderItem.IngredientCustomizations!
            .Single(c => c.IngredientId == _mushrooms.Id);
        mushrooms.IsRemoved.Should().BeFalse();
        mushrooms.Quantity.Should().Be(2);   // the extra survives to the ticket

        var sauce = pizzaOrderItem.IngredientCustomizations!
            .Single(c => c.IngredientId == _tomatoSauce.Id);
        sauce.IsRemoved.Should().BeFalse();

        // Child OrderItem row persists the quantities JSON.
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var childRow = await context.OrderItems.SingleAsync(oi =>
            oi.OrderId == createdOrder.Id && oi.ProductId == _testPizza.Id);
        childRow.ParentOrderItemId.Should().NotBeNull();
        childRow.IngredientQuantitiesJson.Should().Contain($"\"{_cheese.Id}\":0");
        childRow.IngredientQuantitiesJson.Should().Contain($"\"{_mushrooms.Id}\":2");
    }

    [Fact]
    public async Task RegularItem_IngredientHandling_IsUnchanged()
    {
        Client.DefaultRequestHeaders.Add("X-Session-Id", _sessionId);

        // Backfill branch: no quantities map provided, cheese deselected.
        var backfillResponse = await PostAsJsonAsync("/api/basket/items", new AddToBasketDto
        {
            ProductId = _testPizza.Id,
            Quantity = 1,
            SelectedIngredients = new List<Guid> { _tomatoSauce.Id, _mushrooms.Id }
        });
        backfillResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var basket = await ReadResponseAsync<ApiResponse<BasketDto>>(backfillResponse);
        var pizzaItem = basket!.Data!.Items.Single(i => i.ProductId == _testPizza.Id);

        pizzaItem.IngredientQuantities.Should().NotBeNull();
        pizzaItem.IngredientQuantities![_cheese.Id].Should().Be(0);
        pizzaItem.IngredientQuantities[_mushrooms.Id].Should().Be(1);
        pizzaItem.IngredientQuantities[_tomatoSauce.Id].Should().Be(1);

        // As-provided branch: a non-empty quantities map is persisted verbatim,
        // with no backfill — the pre-existing regular-item contract.
        // TestAuthHandler authenticates every request as the same test user, so this
        // add lands in the SAME basket; disambiguate the new line by Id.
        var asProvidedResponse = await PostAsJsonAsync("/api/basket/items", new AddToBasketDto
        {
            ProductId = _testPizza.Id,
            Quantity = 1,
            SelectedIngredients = new List<Guid> { _tomatoSauce.Id, _cheese.Id, _mushrooms.Id },
            IngredientQuantities = new Dictionary<Guid, int> { [_mushrooms.Id] = 2 }
        });
        asProvidedResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var basketTwo = await ReadResponseAsync<ApiResponse<BasketDto>>(asProvidedResponse);
        var pizzaItemTwo = basketTwo!.Data!.Items.Single(
            i => i.ProductId == _testPizza.Id && i.Id != pizzaItem.Id);

        pizzaItemTwo.IngredientQuantities.Should().NotBeNull();
        pizzaItemTwo.IngredientQuantities.Should().HaveCount(1);
        pizzaItemTwo.IngredientQuantities![_mushrooms.Id].Should().Be(2);
    }

    // Slice 1 (#151): the per-option `SelectedSideItems` field was removed from
    // SelectedMenuOptionDto (bundle-child sides were never persisted or displayed).
    // A stale client that still sends it must not break — System.Text.Json ignores
    // the unknown property and the child is built from its ingredient customization
    // only. Sent as raw JSON because the typed DTO no longer carries the field.
    [Fact]
    public async Task BundleChild_LegacyPerOptionSelectedSideItems_AreAcceptedAndIgnored()
    {
        Client.DefaultRequestHeaders.Add("X-Session-Id", _sessionId);

        var json = $$"""
        {
          "productId": "{{_menuProduct.Id}}",
          "quantity": 1,
          "selectedMenuOptions": [
            {
              "sectionId": "{{_mainSection.Id}}",
              "itemId": "{{_testPizza.Id}}",
              "quantity": 1,
              "selectedIngredients": ["{{_tomatoSauce.Id}}", "{{_mushrooms.Id}}"],
              "selectedSideItems": [{ "id": "{{Guid.NewGuid()}}", "quantity": 2 }]
            },
            {
              "sectionId": "{{_drinkSection.Id}}",
              "itemId": "{{_testCola.Id}}",
              "quantity": 1
            }
          ]
        }
        """;

        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        var response = await Client.PostAsync("/api/basket/items", content);

        // The unknown per-option side field is tolerated — the request still succeeds.
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var basket = await ReadResponseAsync<ApiResponse<BasketDto>>(response);
        var pizzaChild = GetChildItem(basket!.Data!, _menuProduct.Id, _testPizza.Id);

        // The option was processed normally: its ingredient customization survives.
        pizzaChild.SelectedIngredients.Should().Contain(new[] { _tomatoSauce.Id, _mushrooms.Id });

        // The ignored per-option side field is not persisted onto the child row —
        // children never carry SelectedSideItemsJson (that column is top-level only),
        // so this fails if anyone ever wires per-option sides into child persistence.
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var childRow = await context.BasketItems.SingleAsync(bi => bi.Id == pizzaChild.Id);
        childRow.SelectedSideItemsJson.Should().BeNull();
    }
}
