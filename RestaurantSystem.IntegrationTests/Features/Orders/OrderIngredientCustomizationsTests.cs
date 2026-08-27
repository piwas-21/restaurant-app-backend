using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

// Slice S0n of SHARED-MODIFIERS-AND-SAUCES-PLAN, plus the guard the S0m prod measurement bought.
//
// Both are about the same root defect: UpdateProductCommand deletes and re-creates every
// ProductIngredient row with a FRESH Guid on each product save, while OrderItem references those
// ingredients BY ID inside IngredientQuantitiesJson. The order line therefore resolves its snapshot
// against a recipe that may no longer contain any of the ids it saved.
//
// 1. THE NAME (S0n). It was read as `GlobalIngredient?.DefaultName ?? ing.Name` — a live catalog
//    read at render time, so renaming a global ingredient reworded receipts and kitchen tickets for
//    orders already placed. Owner ruling, 2026-08-24: a past receipt never changes. Until the
//    OrderItemIngredient snapshot table (slice S1) exists there is nothing frozen to read, so the
//    stop-gap is the per-product name — the same word every cart and guest surface already shows.
//
// 2. THE ALL-ORPHAN GUARD. A snapshot whose ids all died was not merely blanked, it was
//    MISREPORTED: the dead ids were dropped in silence and every CURRENT required ingredient then
//    fell into the "absent from the map = removed" branch, printing "NO Cheese" for an ingredient
//    nobody removed. Measured on prod 2026-08-27 (docs/plans/_research/s0m-prod-ingredient-orphans.md):
//    128 of 183 distinct ids orphaned, 80 of 98 snapshot-carrying lines resolving NOTHING, and 74 of
//    those rendering 147 false removals. Zero lines resolved only partly.
//
// MapToOrderItemDto is a pure in-memory projection, so these drive the entity graph directly —
// no DB round-trip, the same technique as OrderMappingServiceItemKindTests.
[Collection("Database Lane 2")]
public class OrderIngredientCustomizationsTests : IntegrationTestBase
{
    private const string CheeseLocalName = "Cheese";
    private const string CheeseGlobalName = "Mozzarella";
    private const string SauceLocalName = "Tomato Sauce";

    private static readonly Guid CheeseId = Guid.NewGuid();
    private static readonly Guid SauceId = Guid.NewGuid();

    public OrderIngredientCustomizationsTests(DatabaseFixture databaseFixture)
        : base(databaseFixture)
    {
    }

    // The whole point of the guard. Nothing resolves, so the line says NOTHING — rather than
    // claiming, of every required ingredient in the CURRENT recipe, that the guest took it off.
    // Silence is incomplete; a false "NO Cheese" on a kitchen ticket is wrong.
    [Fact]
    public void AllSavedIdsOrphaned_ReportsNoCustomizationsInsteadOfFalseRemovals()
    {
        var dto = Map(new Dictionary<Guid, int>
        {
            [Guid.NewGuid()] = 1,
            [Guid.NewGuid()] = 1
        });

        dto.IngredientCustomizations.Should().BeNull(
            "not one saved id matches a live ProductIngredient row, so the snapshot no longer says "
            + "anything about this recipe");
    }

    // The same fixture WITHOUT the guard would have produced this, which is what prod renders today
    // on 74 lines. Stated as its own assertion so a future change that removes the guard fails here
    // with the actual defect named, not merely with a null/empty mismatch.
    [Fact]
    public void AllSavedIdsOrphaned_NeverMarksACurrentIngredientRemoved()
    {
        var dto = Map(new Dictionary<Guid, int> { [Guid.NewGuid()] = 1 });

        (dto.IngredientCustomizations ?? new List<OrderItemIngredientDto>())
            .Should().NotContain(c => c.IsRemoved,
                "both cheese and sauce are required and absent from the map — pre-guard they were "
                + "both reported removed");
    }

    // Deliberately NOT guarded: one surviving id means the snapshot is still about this recipe, so
    // every existing rule applies unchanged — including the "required ingredient absent from the
    // map = removed" branch, which is a real removal here. Prod measured ZERO partly-orphaned
    // lines, so this case is about not widening the fix, not about a shape we have seen.
    [Fact]
    public void PartiallyOrphanedSnapshot_KeepsTodaysBehaviourExactly()
    {
        var dto = Map(new Dictionary<Guid, int>
        {
            [CheeseId] = 1,
            [Guid.NewGuid()] = 1
        });

        dto.IngredientCustomizations.Should().NotBeNull();
        dto.IngredientCustomizations!.Should().HaveCount(2);

        var cheese = dto.IngredientCustomizations!.Single(c => c.IngredientId == CheeseId);
        cheese.Quantity.Should().Be(1);
        cheese.IsRemoved.Should().BeFalse();

        var sauce = dto.IngredientCustomizations!.Single(c => c.IngredientId == SauceId);
        sauce.Quantity.Should().Be(0);
        sauce.IsRemoved.Should().BeTrue("a required ingredient absent from a snapshot that still "
            + "resolves is a genuine removal");
    }

    // The clean line: every saved id is live. Untouched by both changes except for the name.
    [Fact]
    public void NoOrphans_MapsEverySavedIngredientUnchanged()
    {
        var dto = Map(new Dictionary<Guid, int>
        {
            [CheeseId] = 1,
            [SauceId] = 0
        });

        dto.IngredientCustomizations.Should().NotBeNull();
        dto.IngredientCustomizations!.Should().HaveCount(2);

        dto.IngredientCustomizations!.Single(c => c.IngredientId == CheeseId).IsRemoved.Should().BeFalse();
        dto.IngredientCustomizations!.Single(c => c.IngredientId == SauceId).IsRemoved.Should().BeTrue(
            "an explicit 0 on a base-recipe ingredient is a removal (IngredientRecipeRules)");
    }

    // S0n itself. Cheese carries a GlobalIngredient named differently from the per-product row; the
    // order line must render the PER-PRODUCT name. Renaming the global is exactly the operation the
    // owner ruled must not reach an order that has already been placed.
    [Fact]
    public void IngredientName_IsThePerProductName_NotTheGlobalOne()
    {
        var dto = Map(new Dictionary<Guid, int> { [CheeseId] = 1, [SauceId] = 1 });

        var cheese = dto.IngredientCustomizations!.Single(c => c.IngredientId == CheeseId);
        cheese.IngredientName.Should().Be(CheeseLocalName);
        cheese.IngredientName.Should().NotBe(CheeseGlobalName);
    }

    // A removal reads the same name as a kept ingredient — the "absent = removed" branch had its own
    // copy of the expression, so it needed its own fix and gets its own assertion.
    [Fact]
    public void RemovedIngredientName_IsAlsoThePerProductName()
    {
        var dto = Map(new Dictionary<Guid, int> { [SauceId] = 1 });

        var cheese = dto.IngredientCustomizations!.Single(c => c.IngredientId == CheeseId);
        cheese.IsRemoved.Should().BeTrue();
        cheese.IngredientName.Should().Be(CheeseLocalName);
    }

    private OrderItemDto Map(Dictionary<Guid, int> savedQuantities)
    {
        using var scope = Factory.Services.CreateScope();
        var mapper = scope.ServiceProvider.GetRequiredService<IOrderMappingService>();
        return mapper.MapToOrderItemDto(BuildLine(savedQuantities));
    }

    // A two-ingredient required recipe, cheese linked to a global whose name differs. Both are
    // required, so both are eligible for the "absent = removed" branch the guard exists to stop.
    private static OrderItem BuildLine(Dictionary<Guid, int> savedQuantities)
    {
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
            Id = CheeseId,
            ProductId = product.Id,
            Name = CheeseLocalName,
            IsOptional = false,
            GlobalIngredient = new GlobalIngredient
            {
                Id = Guid.NewGuid(),
                DefaultName = CheeseGlobalName,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = "test"
            },
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test"
        });

        product.DetailedIngredients.Add(new ProductIngredient
        {
            Id = SauceId,
            ProductId = product.Id,
            Name = SauceLocalName,
            IsOptional = false,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test"
        });

        return new OrderItem
        {
            Id = Guid.NewGuid(),
            ProductId = product.Id,
            Product = product,
            ProductName = product.Name,
            Quantity = 1,
            UnitPrice = 15.00m,
            ItemTotal = 15.00m,
            IngredientQuantitiesJson = System.Text.Json.JsonSerializer.Serialize(savedQuantities),
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test"
        };
    }
}
