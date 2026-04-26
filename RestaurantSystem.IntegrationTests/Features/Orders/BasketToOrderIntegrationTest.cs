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

namespace RestaurantSystem.IntegrationTests.Features.Orders;

public class BasketToOrderIntegrationTest : IntegrationTestBase
{
    private readonly string _sessionId = Guid.NewGuid().ToString();
    private Product _testProduct = null!;
    private Menu _testMenu = null!;

    public BasketToOrderIntegrationTest(DatabaseFixture databaseFixture)
        : base(databaseFixture)
    {
    }

    protected override async Task SeedTestData()
    {
        await base.SeedTestData();

        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        // Get seeded product
        _testProduct = await context.Products
            .FirstAsync(p => p.Name == "Test Pizza");

        // Create and seed a test menu with items
        var menu = new Menu
        {
            Id = Guid.NewGuid(),
            Name = "Lunch Special Menu",
            Description = "Special lunch combo with pizza and drink",
            IsActive = true,
            IsDeleted = false,
            DisplayOrder = 1,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test"
        };

        context.Menus.Add(menu);
        await context.SaveChangesAsync();

        // Add items to the menu
        var menuItems = new List<MenuItem>
        {
            new MenuItem
            {
                Id = Guid.NewGuid(),
                MenuId = menu.Id,
                ProductId = _testProduct.Id,
                Quantity = 1,
                SpecialPrice = 10.99m, // Special price for menu item
                CreatedAt = DateTime.UtcNow,
                CreatedBy = "test"
            },
            new MenuItem
            {
                Id = Guid.NewGuid(),
                MenuId = menu.Id,
                ProductId = (await context.Products.FirstAsync(p => p.Name == "Test Cola")).Id,
                Quantity = 1,
                SpecialPrice = 1.99m, // Special price for drink in menu
                CreatedAt = DateTime.UtcNow,
                CreatedBy = "test"
            }
        };

        context.MenuItems.AddRange(menuItems);
        await context.SaveChangesAsync();

        _testMenu = menu;
    }

    [Fact]
    public async Task Should_Add_Product_And_Menu_To_Basket_Then_Create_Order_Successfully()
    {
        // Arrange - Work in anonymous mode with session ID only
        // Don't authenticate to avoid user ID foreign key issues
        Client.DefaultRequestHeaders.Add("X-Session-Id", _sessionId);

        // Act & Assert - Step 1: Add Product to Basket
        var addProductRequest = new AddToBasketDto
        {
            ProductId = _testProduct.Id,
            Quantity = 2,
            SpecialInstructions = "Extra cheese please"
        };

        var productResponse = await PostAsJsonAsync("/api/basket/items", addProductRequest);
        productResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var basketAfterProduct = await ReadResponseAsync<ApiResponse<BasketDto>>(productResponse);
        basketAfterProduct.Should().NotBeNull();
        basketAfterProduct!.Success.Should().BeTrue();
        basketAfterProduct.Data.Should().NotBeNull();
        basketAfterProduct.Data!.Items.Should().HaveCount(1);
        basketAfterProduct.Data.Items.First().ProductId.Should().Be(_testProduct.Id);
        basketAfterProduct.Data.Items.First().Quantity.Should().Be(2);

        // Act & Assert - Step 2: Add Menu to Basket
        var addMenuRequest = new AddToBasketDto
        {
            MenuId = _testMenu.Id,
            Quantity = 1,
            SpecialInstructions = "No ice in drink"
        };

        var menuResponse = await PostAsJsonAsync("/api/basket/items", addMenuRequest);
        menuResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var basketAfterMenu = await ReadResponseAsync<ApiResponse<BasketDto>>(menuResponse);
        basketAfterMenu.Should().NotBeNull();
        basketAfterMenu!.Success.Should().BeTrue();
        basketAfterMenu.Data.Should().NotBeNull();
        basketAfterMenu.Data!.Items.Should().HaveCount(2);

        // Verify both items are in basket
        var productItem = basketAfterMenu.Data.Items.FirstOrDefault(i => i.ProductId == _testProduct.Id);
        var menuItem = basketAfterMenu.Data.Items.FirstOrDefault(i => i.MenuId == _testMenu.Id);

        productItem.Should().NotBeNull();
        menuItem.Should().NotBeNull();
        menuItem!.UnitPrice.Should().Be(12.98m); // 10.99 + 1.99 (special menu prices)

        // Act & Assert - Step 3: Get Basket Summary
        var summaryResponse = await Client.GetAsync("/api/basket/summary");
        summaryResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var summary = await ReadResponseAsync<ApiResponse<BasketSummaryDto>>(summaryResponse);
        summary.Should().NotBeNull();
        summary!.Success.Should().BeTrue();
        summary.Data.Should().NotBeNull();
        summary.Data!.ItemCount.Should().Be(3); // 2 products + 1 menu
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
                new CreateOrderItemDto
                {
                    ProductId = _testProduct.Id,
                    Quantity = 2,
                    SpecialInstructions = "Extra cheese please"
                },
                new CreateOrderItemDto
                {
                    MenuId = _testMenu.Id,
                    Quantity = 1,
                    SpecialInstructions = "No ice in drink"
                }
            },
            Payments = new List<CreateOrderPaymentDto>
            {
                new CreateOrderPaymentDto
                {
                    PaymentMethod = PaymentMethod.Cash,
                    Amount = 50.00m // Assuming total is less than this
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
        createdOrder.Items.Should().HaveCount(2);

        // Verify Order Items
        var orderProductItem = createdOrder.Items.FirstOrDefault(i => i.ProductId == _testProduct.Id);
        var orderMenuItem = createdOrder.Items.FirstOrDefault(i => i.ProductId != _testProduct.Id); // Menu items will have product IDs from the menu

        orderProductItem.Should().NotBeNull();
        orderProductItem!.Quantity.Should().Be(2);
        orderProductItem.ProductName.Should().Be("Test Pizza");

        // Note: OrderItemDto doesn't have MenuId, but we can verify by quantity
        createdOrder.Items.Should().HaveCount(2);

        // Verify Order Totals
        createdOrder.SubTotal.Should().BeGreaterThan(0);
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
        orderInDb!.Items.Should().HaveCount(2);
        orderInDb.Payments.Should().HaveCount(1);
        orderInDb.OrderNumber.Should().Be(createdOrder.OrderNumber);
    }

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

        // Assert - Should fail or return appropriate error
        // The actual behavior depends on your validation logic
        if (response.StatusCode == HttpStatusCode.OK)
        {
            var result = await ReadResponseAsync<ApiResponse<OrderDto>>(response);
            result!.Success.Should().BeFalse();
            result.Message.Should().NotBeNullOrEmpty();
        }
        else
        {
            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }
    }

    [Fact]
    public async Task Should_Calculate_Correct_Totals_With_Multiple_Items()
    {
        // Arrange - Work in anonymous mode
        Client.DefaultRequestHeaders.Add("X-Session-Id", _sessionId);

        // Add multiple products to basket
        var addProductRequest1 = new AddToBasketDto
        {
            ProductId = _testProduct.Id,
            Quantity = 3
        };

        await PostAsJsonAsync("/api/basket/items", addProductRequest1);

        var addMenuRequest = new AddToBasketDto
        {
            MenuId = _testMenu.Id,
            Quantity = 2
        };

        await PostAsJsonAsync("/api/basket/items", addMenuRequest);

        // Get basket to verify totals
        var basketResponse = await Client.GetAsync("/api/basket");
        basketResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var basket = await ReadResponseAsync<ApiResponse<BasketDto>>(basketResponse);
        basket!.Data.Should().NotBeNull();

        // Calculate expected totals
        var expectedProductTotal = _testProduct.BasePrice * 3;
        var expectedMenuTotal = 12.98m * 2; // Menu special price
        var expectedSubTotal = expectedProductTotal + expectedMenuTotal;

        basket.Data!.SubTotal.Should().Be(expectedSubTotal);
        basket.Data.Total.Should().BeGreaterThanOrEqualTo(expectedSubTotal); // May include tax/fees
    }
}
