using FluentAssertions;
using Moq;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

/// <summary>
/// Pins the ItemTotal computation contract for OrderItemFactory against
/// the BasketService convention. See issue #54: BasketService.AddItemToBasketAsync
/// (Features/Basket/Services/BasketService.cs:230-231) zeros child basket items'
/// ItemTotal because the parent already carries the rolled-up combo price.
/// OrderItemFactory must follow the same convention, otherwise the legacy
/// compute path in OrderPricingService (no command.BasketSubTotal) double-counts
/// every child's UnitPrice on top of the parent.
/// </summary>
[Collection("Database")]
public class OrderItemFactoryTests : IAsyncLifetime
{
    private readonly DatabaseFixture _fixture;
    private ApplicationDbContext _context = null!;
    private OrderItemFactory _factory = null!;
    private Mock<ICurrentUserService> _currentUserServiceMock = null!;

    public OrderItemFactoryTests(DatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        await _fixture.ResetDatabaseAsync();

        _context = _fixture.CreateContext();
        _currentUserServiceMock = new Mock<ICurrentUserService>();
        _currentUserServiceMock.Setup(x => x.UserId).Returns(Guid.NewGuid());
        _currentUserServiceMock.Setup(x => x.GetAuditIdentifier()).Returns("test-user");

        _factory = new OrderItemFactory(_context, _currentUserServiceMock.Object);
    }

    public async Task DisposeAsync()
    {
        await _context.DisposeAsync();
    }

    [Fact]
    public async Task AddItemAsync_ProductWithoutChildren_SetsItemTotalFromUnitPriceAndQuantity()
    {
        // Arrange
        var product = await SeedProductAsync("Standalone Pizza", basePrice: 12.50m);
        var order = new Order { OrderNumber = "T-1", CreatedBy = "test" };
        var dto = new CreateOrderItemDto
        {
            ProductId = product.Id,
            Quantity = 2,
            UnitPrice = product.BasePrice
        };

        // Act
        var error = await _factory.AddItemAsync(order, dto, CancellationToken.None);

        // Assert
        error.Should().BeNull();
        order.Items.Should().HaveCount(1);
        var item = order.Items.Single();
        item.UnitPrice.Should().Be(12.50m);
        item.Quantity.Should().Be(2);
        item.ItemTotal.Should().Be(25.00m); // unitPrice * qty
        item.ParentOrderItem.Should().BeNull();
    }

    [Fact]
    public async Task AddItemAsync_ProductWithSinglePricedChild_ParentCarriesFullTotal_ChildItemTotalIsZero()
    {
        // Arrange — mirrors the BasketService convention (BasketService.cs:230-231):
        // a parent menu with a priced child option. The parent's UnitPrice is the
        // rolled-up combo price (BasePrice + child additional, computed by the
        // caller). The child's UnitPrice is preserved for display, but its
        // ItemTotal must be 0 so the items-sum doesn't double-count.
        var parentProduct = await SeedProductAsync("Lunch Combo", basePrice: 15.00m);
        var childProduct = await SeedProductAsync("Cola", basePrice: 3.00m);
        const decimal childAdditional = 2.50m;
        const decimal parentRolledUpPrice = 17.50m; // BasePrice + childAdditional

        var order = new Order { OrderNumber = "T-2", CreatedBy = "test" };
        var dto = new CreateOrderItemDto
        {
            ProductId = parentProduct.Id,
            Quantity = 1,
            UnitPrice = parentRolledUpPrice,
            ChildItems = new List<CreateOrderItemDto>
            {
                new()
                {
                    ProductId = childProduct.Id,
                    Quantity = 1,
                    UnitPrice = childAdditional
                }
            }
        };

        // Act
        var error = await _factory.AddItemAsync(order, dto, CancellationToken.None);

        // Assert
        error.Should().BeNull();
        order.Items.Should().HaveCount(2);

        var parent = order.Items.Single(i => i.ProductId == parentProduct.Id);
        parent.ParentOrderItem.Should().BeNull();
        parent.UnitPrice.Should().Be(parentRolledUpPrice);
        parent.ItemTotal.Should().Be(parentRolledUpPrice); // qty 1

        var child = order.Items.Single(i => i.ProductId == childProduct.Id);
        child.ParentOrderItem.Should().BeSameAs(parent);
        child.UnitPrice.Should().Be(childAdditional); // preserved for display
        child.ItemTotal.Should().Be(0m);              // zeroed — issue #54 contract

        // Items-sum equals the parent total; no double-counting.
        order.Items.Sum(i => i.ItemTotal).Should().Be(parentRolledUpPrice);
    }

    [Fact]
    public async Task AddItemAsync_ProductWithMultiplePricedChildren_AllChildItemTotalsAreZero()
    {
        // Arrange
        var parentProduct = await SeedProductAsync("Lunch Combo", basePrice: 15.00m);
        var pizzaChild = await SeedProductAsync("Margherita", basePrice: 10.00m);
        var colaChild = await SeedProductAsync("Cola", basePrice: 3.00m);
        var saladChild = await SeedProductAsync("Side Salad", basePrice: 4.00m);
        const decimal parentRolledUpPrice = 22.00m;

        var order = new Order { OrderNumber = "T-3", CreatedBy = "test" };
        var dto = new CreateOrderItemDto
        {
            ProductId = parentProduct.Id,
            Quantity = 1,
            UnitPrice = parentRolledUpPrice,
            ChildItems = new List<CreateOrderItemDto>
            {
                new() { ProductId = pizzaChild.Id, Quantity = 1, UnitPrice = 5.00m },
                new() { ProductId = colaChild.Id,  Quantity = 1, UnitPrice = 1.50m },
                new() { ProductId = saladChild.Id, Quantity = 1, UnitPrice = 0.50m }
            }
        };

        // Act
        await _factory.AddItemAsync(order, dto, CancellationToken.None);

        // Assert
        order.Items.Should().HaveCount(4);
        order.Items.Where(i => i.ParentOrderItem != null).Should().HaveCount(3);
        order.Items
            .Where(i => i.ParentOrderItem != null)
            .Select(i => i.ItemTotal)
            .Should().OnlyContain(t => t == 0m);

        // Items-sum is the parent's rolled-up price only — no child contributions.
        order.Items.Sum(i => i.ItemTotal).Should().Be(parentRolledUpPrice);
    }

    [Fact]
    public async Task AddItemAsync_NestedChildItems_OnlyTopLevelParentContributes()
    {
        // Arrange — nested case (grandchild). Only the top-level parent (no
        // ParentOrderItem) contributes to items-sum; every descendant must
        // carry ItemTotal == 0 because the top parent's UnitPrice is the
        // fully rolled-up price for the whole tree.
        var rootProduct = await SeedProductAsync("Family Meal", basePrice: 30.00m);
        var midProduct = await SeedProductAsync("Combo Plate", basePrice: 12.00m);
        var leafProduct = await SeedProductAsync("Extra Sauce", basePrice: 1.00m);
        const decimal rootRolledUpPrice = 40.00m;

        var order = new Order { OrderNumber = "T-4", CreatedBy = "test" };
        var dto = new CreateOrderItemDto
        {
            ProductId = rootProduct.Id,
            Quantity = 1,
            UnitPrice = rootRolledUpPrice,
            ChildItems = new List<CreateOrderItemDto>
            {
                new()
                {
                    ProductId = midProduct.Id,
                    Quantity = 1,
                    UnitPrice = 8.00m,
                    ChildItems = new List<CreateOrderItemDto>
                    {
                        new()
                        {
                            ProductId = leafProduct.Id,
                            Quantity = 1,
                            UnitPrice = 2.00m
                        }
                    }
                }
            }
        };

        // Act
        await _factory.AddItemAsync(order, dto, CancellationToken.None);

        // Assert
        order.Items.Should().HaveCount(3);
        order.Items.Where(i => i.ParentOrderItem == null).Should().HaveCount(1);
        order.Items.Where(i => i.ParentOrderItem != null).Should().HaveCount(2);
        order.Items
            .Where(i => i.ParentOrderItem != null)
            .Select(i => i.ItemTotal)
            .Should().OnlyContain(t => t == 0m);

        order.Items.Sum(i => i.ItemTotal).Should().Be(rootRolledUpPrice);
    }

    [Fact]
    public async Task AddItemAsync_ChildWithCustomizationPrice_IsRolledUpIntoParentItemTotal()
    {
        // Arrange — regression guard for PR #59 gemini review.
        // BasketService (Features/Basket/Services/BasketService.cs:215, 243-245)
        // adds each child's customization price into the parent's rolled-up
        // total. OrderItem has no CustomizationPrice column, so OrderItemFactory
        // must add the child's CustomizationPrice directly onto the parent's
        // ItemTotal — otherwise child customization (e.g. extra toppings on a
        // child pizza option) is silently dropped.
        var parentProduct = await SeedProductAsync("Lunch Combo", basePrice: 15.00m);
        var childProduct = await SeedProductAsync("Build-Your-Own Pizza", basePrice: 10.00m);
        const decimal parentRolledUpPrice = 17.50m; // BasePrice + child additional
        const decimal childCustomization = 2.75m;   // e.g. extra cheese + olives

        var order = new Order { OrderNumber = "T-5", CreatedBy = "test" };
        var dto = new CreateOrderItemDto
        {
            ProductId = parentProduct.Id,
            Quantity = 1,
            UnitPrice = parentRolledUpPrice,
            ChildItems = new List<CreateOrderItemDto>
            {
                new()
                {
                    ProductId = childProduct.Id,
                    Quantity = 1,
                    UnitPrice = 2.50m,
                    CustomizationPrice = childCustomization
                }
            }
        };

        // Act
        await _factory.AddItemAsync(order, dto, CancellationToken.None);

        // Assert
        order.Items.Should().HaveCount(2);
        var parent = order.Items.Single(i => i.ParentOrderItem == null);
        var child = order.Items.Single(i => i.ParentOrderItem != null);

        // Child row stays zeroed (BasketService convention, no double-count).
        child.ItemTotal.Should().Be(0m);

        // Parent absorbs the child's customization price.
        parent.ItemTotal.Should().Be(parentRolledUpPrice + childCustomization);

        // Items-sum equals exactly the rolled-up parent total (including
        // customization) — no double-count, no dropped customization.
        order.Items.Sum(i => i.ItemTotal).Should().Be(parentRolledUpPrice + childCustomization);
    }

    [Fact]
    public async Task AddItemAsync_GrandchildWithCustomizationPrice_IsRolledUpIntoRoot_NotImmediateParent()
    {
        // Arrange — PR #67 review regression guard.
        // At grandchild depth, the immediate parent's ItemTotal must stay 0
        // (BasketService convention). If a grandchild's CustomizationPrice is
        // rolled into its immediate parent (the child), the next level
        // silently drops it. The fix walks up to the root and accumulates
        // CustomizationPrice there so items-sum equals the rolled-up root
        // total + the grandchild's customization.
        var rootProduct = await SeedProductAsync("Family Meal", basePrice: 30.00m);
        var midProduct = await SeedProductAsync("Combo Plate", basePrice: 12.00m);
        var leafProduct = await SeedProductAsync("Build-Your-Own Pizza", basePrice: 10.00m);
        const decimal rootRolledUpPrice = 40.00m;
        const decimal grandchildCustomization = 3.25m; // e.g. extra cheese + olives

        var order = new Order { OrderNumber = "T-6", CreatedBy = "test" };
        var dto = new CreateOrderItemDto
        {
            ProductId = rootProduct.Id,
            Quantity = 1,
            UnitPrice = rootRolledUpPrice,
            ChildItems = new List<CreateOrderItemDto>
            {
                new()
                {
                    ProductId = midProduct.Id,
                    Quantity = 1,
                    UnitPrice = 8.00m,
                    ChildItems = new List<CreateOrderItemDto>
                    {
                        new()
                        {
                            ProductId = leafProduct.Id,
                            Quantity = 1,
                            UnitPrice = 2.00m,
                            CustomizationPrice = grandchildCustomization
                        }
                    }
                }
            }
        };

        // Act
        await _factory.AddItemAsync(order, dto, CancellationToken.None);

        // Assert
        order.Items.Should().HaveCount(3);

        var root = order.Items.Single(i => i.ProductId == rootProduct.Id);
        var mid = order.Items.Single(i => i.ProductId == midProduct.Id);
        var leaf = order.Items.Single(i => i.ProductId == leafProduct.Id);

        root.ParentOrderItem.Should().BeNull();
        mid.ParentOrderItem.Should().BeSameAs(root);
        leaf.ParentOrderItem.Should().BeSameAs(mid);

        // Root absorbs the grandchild's CustomizationPrice — not the mid parent.
        root.ItemTotal.Should().Be(rootRolledUpPrice + grandchildCustomization);
        mid.ItemTotal.Should().Be(0m);
        leaf.ItemTotal.Should().Be(0m);

        // Items-sum equals the rolled-up root total plus the customization —
        // nothing dropped, nothing double-counted.
        order.Items.Sum(i => i.ItemTotal).Should().Be(rootRolledUpPrice + grandchildCustomization);
    }

    private async Task<Product> SeedProductAsync(string name, decimal basePrice)
    {
        var product = new Product
        {
            Id = Guid.NewGuid(),
            Name = name,
            Description = $"{name} description",
            BasePrice = basePrice,
            Type = ProductType.MainItem,
            IsActive = true,
            IsAvailable = true,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test"
        };
        _context.Products.Add(product);
        await _context.SaveChangesAsync();
        return product;
    }
}
