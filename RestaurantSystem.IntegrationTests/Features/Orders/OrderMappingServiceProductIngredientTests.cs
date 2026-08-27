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
[Collection("Database Lane 3")]
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
                // Included in the base recipe, so deselecting it (qty 0) is a genuine
                // removal → IsRemoved=true (see the removal-semantics test below).
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

        // Assert — the product's ingredient customizations survived the mapping, and the
        // deepest (GlobalIngredient) load level actually ran.
        //
        // That last claim used to be proved by the DTO name coming back "Mozzarella". S0n
        // removed that evidence: the order line now renders ProductIngredient.Name
        // unconditionally, so a rename of the global cannot reword a placed order, and no
        // DTO field reflects the GlobalIngredient level any more. The load is still made
        // (see EnsureProductIngredientsLoadedAsync) and is still what #161 was about, so the
        // proof moves to the entity graph itself — which is a direct assertion on the
        // behaviour rather than an inference from a rendered string.
        var trackedCheeseAfter = trackedItem.Product!.DetailedIngredients.Single(pi => pi.Id == cheeseId);
        context.Entry(trackedCheeseAfter).Reference(i => i.GlobalIngredient).IsLoaded
            .Should().BeTrue("the mapper must load the GlobalIngredient level even when "
                + "DetailedIngredients was already tracked (#161)");
        trackedCheeseAfter.GlobalIngredient!.DefaultName.Should().Be("Mozzarella");

        var mappedItem = dto.Items.Single();
        mappedItem.ProductId.Should().Be(productId);
        mappedItem.IngredientCustomizations.Should().NotBeNull();
        mappedItem.IngredientCustomizations!.Should().HaveCount(2);

        var cheese = mappedItem.IngredientCustomizations!.Single(c => c.IngredientId == cheeseId);
        cheese.Quantity.Should().Be(0);
        cheese.IsRemoved.Should().BeTrue();
        // The PER-PRODUCT name, although this ingredient has a global one saying
        // "Mozzarella" (asserted on the entity above). S0n: an order line never reads the
        // global, so renaming it cannot reword a receipt that is already printed.
        cheese.IngredientName.Should().Be("Cheese");

        var sauce = mappedItem.IngredientCustomizations!.Single(c => c.IngredientId == sauceId);
        sauce.Quantity.Should().Be(1);
        sauce.IsRemoved.Should().BeFalse();
        sauce.IngredientName.Should().Be("Tomato Sauce");
    }

    // §9.18 — soft-deleting the GlobalIngredient behind an ingredient must not change what an
    // ALREADY-PLACED order, and therefore the kitchen ticket, says. Orders read through the
    // global query filter (nothing in the order graph uses IgnoreQueryFilters), so the
    // navigation resolves to null for a soft-deleted global.
    //
    // This test used to pin a FALLBACK — `GlobalIngredient?.DefaultName ?? ing.Name` — and said
    // of it: "If that is ever judged wrong, this test is where the decision is recorded."
    // The decision arrived. The owner ruled on 2026-08-24 that a past receipt never changes, so
    // S0n dropped the global from the expression entirely; the local name is no longer a
    // fallback, it is the only answer. What the test proves is therefore stronger and simpler
    // than before: the deletion is invisible to a placed order. Kept because it is the
    // printer-app-visible surface (#161's sibling) and because a null navigation on this path
    // must stay harmless.
    [Fact]

    public async Task MapToOrderDtoAsync_GlobalIngredientSoftDeleted_StillRendersTheLocalName()
    {
        var cheeseId = Guid.NewGuid();
        Guid orderId;

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
                IsOptional = false,
                IsIncludedInBasePrice = true,
                GlobalIngredientId = globalCheese.Id,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = "test"
            });

            var order = new Order
            {
                Id = Guid.NewGuid(),
                OrderNumber = "TEST-9-18",
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
                    new Dictionary<Guid, int> { [cheeseId] = 1 }),
                CreatedAt = DateTime.UtcNow,
                CreatedBy = "test"
            });

            seed.AddRange(globalCheese, product, order);
            await seed.SaveChangesAsync();
            orderId = order.Id;

            // Soft-delete exactly as DeleteGlobalIngredientCommand now does — by setting the
            // flag, not via Remove(): Remove() + SaveChangesAsync still HARD-deletes (that is
            // §9.18's unfixed root half), which here would hit the FK and throw instead.
            globalCheese.IsDeleted = true;
            globalCheese.DeletedAt = DateTime.UtcNow;
            await seed.SaveChangesAsync();
        }

        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var mapper = scope.ServiceProvider.GetRequiredService<IOrderMappingService>();

        var loadedOrder = await context.Orders
            .Include(o => o.Items).ThenInclude(i => i.Product).ThenInclude(p => p!.DetailedIngredients)
            .FirstAsync(o => o.Id == orderId);

        var dto = await mapper.MapToOrderDtoAsync(loadedOrder);

        var cheese = dto.Items.Single().IngredientCustomizations!.Single(c => c.IngredientId == cheeseId);
        // Not "Mozzarella", and — since S0n — not because the global went away: the first test
        // in this class pins the SAME answer while the global is live and named differently, so
        // the pair brackets the behaviour on both sides.
        cheese.IngredientName.Should().Be("Cheese");
    }

    // "Removed" (a "NO X" kitchen-ticket line) applies ONLY to base-recipe
    // ingredients at qty 0 — a required ingredient, or an optional one included in
    // the base price. A non-included optional (a paid add-on) at qty 0 was never
    // added, so it must NOT print "NO X". Pins the exact staging scenario that
    // surfaced the bug: a combo's Margherita with dough kept, the included-in-base
    // mozzarella deselected, and the not-included olives add-on simply not chosen.
    [Fact]
    public async Task MapToOrderDtoAsync_QtyZero_IsRemovedOnlyForBaseRecipeIngredients()
    {
        var doughId = Guid.NewGuid();
        var mozzarellaId = Guid.NewGuid();
        var olivesId = Guid.NewGuid();
        Guid orderId;

        using (var seedScope = Factory.Services.CreateScope())
        {
            var seed = seedScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

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
            // Required, in the base.
            product.DetailedIngredients.Add(new ProductIngredient
            {
                Id = doughId,
                ProductId = product.Id,
                Name = "Dough",
                IsOptional = false,
                IsIncludedInBasePrice = true,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = "test"
            });
            // Optional but included in the base recipe → deselecting is a real removal.
            product.DetailedIngredients.Add(new ProductIngredient
            {
                Id = mozzarellaId,
                ProductId = product.Id,
                Name = "Mozzarella",
                IsOptional = true,
                IsIncludedInBasePrice = true,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = "test"
            });
            // Optional, NOT included (a paid add-on) → qty 0 means "not added", not removed.
            product.DetailedIngredients.Add(new ProductIngredient
            {
                Id = olivesId,
                ProductId = product.Id,
                Name = "Olives",
                IsOptional = true,
                IsIncludedInBasePrice = false,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = "test"
            });

            var order = new Order
            {
                Id = Guid.NewGuid(),
                OrderNumber = "TEST-REMOVAL",
                Type = OrderType.Takeaway,
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
                // The unified sheet sends every option's quantity, including 0 for
                // unselected ones: dough kept, mozzarella deselected, olives not added.
                IngredientQuantitiesJson = System.Text.Json.JsonSerializer.Serialize(
                    new Dictionary<Guid, int> { [doughId] = 1, [mozzarellaId] = 0, [olivesId] = 0 }),
                CreatedAt = DateTime.UtcNow,
                CreatedBy = "test"
            });

            seed.AddRange(product, order);
            await seed.SaveChangesAsync();
            orderId = order.Id;
        }

        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var mapper = scope.ServiceProvider.GetRequiredService<IOrderMappingService>();

        var loadedOrder = await context.Orders
            .Include(o => o.Items).ThenInclude(i => i.Product).ThenInclude(p => p!.DetailedIngredients)
            .FirstAsync(o => o.Id == orderId);

        var dto = await mapper.MapToOrderDtoAsync(loadedOrder);

        var customizations = dto.Items.Single().IngredientCustomizations!;
        // Dough kept (part of the base) — present, not removed.
        customizations.Single(c => c.IngredientId == doughId).IsRemoved.Should().BeFalse();
        // Mozzarella is included-in-base and deselected → a genuine removal ("NO Mozzarella").
        customizations.Single(c => c.IngredientId == mozzarellaId).IsRemoved.Should().BeTrue();
        // Olives is a non-included add-on at qty 0 → NOT removed (no "NO Olives" on the ticket).
        customizations.Single(c => c.IngredientId == olivesId).IsRemoved.Should().BeFalse();
    }
}
