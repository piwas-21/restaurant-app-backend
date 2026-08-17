using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

// Issue #153: OrderMappingService.MapToOrderDtoAsync skipped loading a legacy
// Menu item's DetailedIngredients whenever the OrderItem.Menu reference was
// already tracked in the same context. EF relationship fixup marks a
// same-context reference navigation loaded, so the previously nested IsLoaded
// checks fell through and IngredientCustomizations came back empty. Same defect
// class as the Product branch fixed in #152, on the standalone Menu (MenuId)
// path that no live HTTP flow exercises — hence this direct mapper test.
[Collection("Database Lane 1")]
public class OrderMappingServiceMenuIngredientTests : IntegrationTestBase
{
    public OrderMappingServiceMenuIngredientTests(DatabaseFixture databaseFixture)
        : base(databaseFixture)
    {
    }

    [Fact]
    public async Task MapToOrderDtoAsync_MenuReferenceAlreadyTracked_PopulatesIngredientCustomizations()
    {
        // Cheese carries a GlobalIngredient so its mapped name proves the deepest
        // load level (MenuItems -> Product -> DetailedIngredients -> GlobalIngredient)
        // actually ran; sauce is a plain required ingredient.
        var cheeseId = Guid.NewGuid();
        var sauceId = Guid.NewGuid();
        Guid menuId;
        Guid orderId;

        // Arrange — seed a standalone Menu whose first MenuItem's Product carries
        // detailed ingredients, plus an Order whose single OrderItem uses the
        // legacy MenuId path with an ingredient-quantities snapshot (cheese
        // removed = 0, sauce kept = 1).
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
                Name = "Chief's Special Base",
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
                // Included in the base recipe, so qty 0 is a genuine removal → IsRemoved=true
                // (a non-included add-on at qty 0 is "not added", not removed).
                IsIncludedInBasePrice = true,
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

            var menu = new Menu
            {
                Id = Guid.NewGuid(),
                Name = "Chief's Special",
                Date = DateOnly.FromDateTime(DateTime.UtcNow),
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = "test"
            };
            menu.MenuItems.Add(new MenuItem
            {
                Id = Guid.NewGuid(),
                MenuId = menu.Id,
                ProductId = product.Id,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = "test"
            });

            var order = new Order
            {
                Id = Guid.NewGuid(),
                OrderNumber = "TEST-153",
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
                MenuId = menu.Id,
                ProductName = "Chief's Special",
                Quantity = 1,
                UnitPrice = 15.00m,
                ItemTotal = 15.00m,
                IngredientQuantitiesJson = System.Text.Json.JsonSerializer.Serialize(
                    new Dictionary<Guid, int> { [cheeseId] = 0, [sauceId] = 1 }),
                CreatedAt = DateTime.UtcNow,
                CreatedBy = "test"
            });

            seed.AddRange(globalCheese, product, menu, order);
            await seed.SaveChangesAsync();

            menuId = menu.Id;
            orderId = order.Id;
        }

        // Act — resolve the mapper against a fresh scope, load the order (Items
        // included but the Menu reference NOT), then pre-track the Menu on its own
        // so EF fixup wires it onto the OrderItem and marks the reference loaded.
        // That reproduces the create-path condition the bug needs: Menu reference
        // reads as loaded while its MenuItems are not.
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var mapper = scope.ServiceProvider.GetRequiredService<IOrderMappingService>();

        var loadedOrder = await context.Orders
            .Include(o => o.Items)
            .FirstAsync(o => o.Id == orderId);
        await context.Menus.FirstAsync(m => m.Id == menuId);

        // Guard: without these two conditions holding we would not be exercising
        // the bug (the un-fixed code would have entered its load block anyway).
        var trackedItem = loadedOrder.Items.Single();
        context.Entry(trackedItem).Reference(i => i.Menu).IsLoaded.Should().BeTrue();
        context.Entry(trackedItem.Menu!).Collection(m => m.MenuItems).IsLoaded.Should().BeFalse();

        var dto = await mapper.MapToOrderDtoAsync(loadedOrder);

        // Assert — the menu item's ingredient customizations survived the mapping.
        // Pre-fix this list is null (the nested loads were skipped); post-fix it is
        // populated from the menu's product ingredients.
        var mappedItem = dto.Items.Single();
        mappedItem.MenuID.Should().Be(menuId);
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
    }
}
