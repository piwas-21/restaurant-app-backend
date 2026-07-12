using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

// Issue #161: OrderMappingService.MapToOrderDtoAsync nested the per-ingredient
// GlobalIngredient load loop inside the "DetailedIngredients not loaded" guard.
// When a Product was already tracked with its DetailedIngredients (EF relationship
// fixup marks the Product reference AND the collection loaded) but the child
// GlobalIngredient references were not, the outer guard fell through and the
// GlobalIngredient loads were skipped — ingredient names silently fell back to
// ProductIngredient.Name. Same fixup-loaded defect class fixed shallowly for the
// reference navigations in #152 (Product) and #153 (Menu), pushed one level deeper
// to the GlobalIngredient reference. This test pins the Product (ProductId) branch;
// its Menu sibling lives in OrderMappingServiceMenuIngredientTests.
public class OrderMappingServiceProductIngredientTests : IntegrationTestBase
{
    public OrderMappingServiceProductIngredientTests(DatabaseFixture databaseFixture)
        : base(databaseFixture)
    {
    }

    [Fact]
    public async Task MapToOrderDtoAsync_ProductDetailedIngredientsAlreadyTracked_ResolvesGlobalIngredientName()
    {
        // Cheese carries a GlobalIngredient so its mapped name proves the deepest
        // load level (Product -> DetailedIngredients -> GlobalIngredient) actually
        // ran; sauce is a plain required ingredient without one.
        var cheeseId = Guid.NewGuid();
        var sauceId = Guid.NewGuid();
        Guid productId;
        Guid orderId;

        // Arrange — seed a Product carrying detailed ingredients (cheese linked to a
        // GlobalIngredient), plus an Order whose single OrderItem uses the ProductId
        // path with an ingredient-quantities snapshot (cheese removed = 0, sauce
        // kept = 1).
        using (var seedScope = Factory.Services.CreateScope())
        {
            var seed = seedScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var globalCheese = new GlobalIngredient
            {
                Id = Guid.NewGuid(),
                DefaultName = "Mozzarella",
                CreatedAt = DateTime.UtcNow,
                CreatedBy = "test"
            };

            var product = new Product
            {
                Id = Guid.NewGuid(),
                Name = "Margherita Pizza",
                BasePrice = 15.00m,
                Type = ProductType.MainItem,
                Ingredients = new List<string>(),
                Allergens = new List<string>(),
                CreatedAt = DateTime.UtcNow,
                CreatedBy = "test"
            };
            product.DetailedIngredients.Add(new ProductIngredient
            {
                Id = cheeseId,
                ProductId = product.Id,
                Name = "Cheese",
                IsOptional = true,
                GlobalIngredientId = globalCheese.Id,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = "test"
            });
            product.DetailedIngredients.Add(new ProductIngredient
            {
                Id = sauceId,
                ProductId = product.Id,
                Name = "Tomato Sauce",
                IsOptional = false,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = "test"
            });

            var order = new Order
            {
                Id = Guid.NewGuid(),
                OrderNumber = "TEST-161",
                Type = OrderType.DineIn,
                Status = OrderStatus.Confirmed,
                PaymentStatus = PaymentStatus.Pending,
                OrderDate = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = "test"
            };
            order.Items.Add(new OrderItem
            {
                Id = Guid.NewGuid(),
                OrderId = order.Id,
                ProductId = product.Id,
                ProductName = "Margherita Pizza",
                Quantity = 1,
                UnitPrice = 15.00m,
                ItemTotal = 15.00m,
                IngredientQuantitiesJson = System.Text.Json.JsonSerializer.Serialize(
                    new Dictionary<Guid, int> { [cheeseId] = 0, [sauceId] = 1 }),
                CreatedAt = DateTime.UtcNow,
                CreatedBy = "test"
            });

            seed.AddRange(globalCheese, product, order);
            await seed.SaveChangesAsync();

            productId = product.Id;
            orderId = order.Id;
        }

        // Act — resolve the mapper against a fresh scope, load the order (Items
        // included but the Product reference NOT), then pre-track the Product WITH its
        // DetailedIngredients on its own. The explicit Include marks that Product's
        // DetailedIngredients collection loaded, and EF relationship fixup wires the
        // Product onto the OrderItem and marks the item's Product reference loaded —
        // while the child GlobalIngredient references stay unloaded. That is the deeper
        // create-path condition the bug needs: the outer "DetailedIngredients not
        // loaded" guard reads false, so the un-fixed nested code never reaches the
        // GlobalIngredient load loop.
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var mapper = scope.ServiceProvider.GetRequiredService<IOrderMappingService>();

        var loadedOrder = await context.Orders
            .Include(o => o.Items)
            .FirstAsync(o => o.Id == orderId);
        await context.Products
            .Include(p => p.DetailedIngredients)
            .FirstAsync(p => p.Id == productId);

        // Guard: reproduce the exact latent state. Without all three holding we would
        // not be exercising the one-level-deeper bug (the un-fixed code would have
        // entered its load block anyway and loaded the GlobalIngredient).
        var trackedItem = loadedOrder.Items.Single();
        context.Entry(trackedItem).Reference(i => i.Product).IsLoaded.Should().BeTrue();
        context.Entry(trackedItem.Product!).Collection(p => p.DetailedIngredients).IsLoaded.Should().BeTrue();
        var trackedCheese = trackedItem.Product!.DetailedIngredients.Single(pi => pi.Id == cheeseId);
        context.Entry(trackedCheese).Reference(i => i.GlobalIngredient).IsLoaded.Should().BeFalse();

        var dto = await mapper.MapToOrderDtoAsync(loadedOrder);

        // Assert — the product's ingredient customizations survived the mapping and the
        // cheese name resolved from GlobalIngredient.DefaultName. Pre-fix the deepest
        // load was skipped, so cheese.IngredientName fell back to "Cheese".
        var mappedItem = dto.Items.Single();
        mappedItem.ProductId.Should().Be(productId);
        mappedItem.IngredientCustomizations.Should().NotBeNull();
        mappedItem.IngredientCustomizations!.Should().HaveCount(2);

        var cheese = mappedItem.IngredientCustomizations!.Single(c => c.IngredientId == cheeseId);
        cheese.Quantity.Should().Be(0);
        cheese.IsRemoved.Should().BeTrue();
        // Name resolved from GlobalIngredient.DefaultName proves the deepest
        // (GlobalIngredient) load ran, not just a fallback to ProductIngredient.Name.
        cheese.IngredientName.Should().Be("Mozzarella");

        var sauce = mappedItem.IngredientCustomizations!.Single(c => c.IngredientId == sauceId);
        sauce.Quantity.Should().Be(1);
        sauce.IsRemoved.Should().BeFalse();
        sauce.IngredientName.Should().Be("Tomato Sauce");
    }
}
