using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

/// <summary>
/// The frozen ingredient rows follow the recipe order the admin arranged, and follow it the same way
/// every time.
/// </summary>
/// <remarks>
/// <para>
/// <b>What was wrong.</b> <c>OrderIngredientSnapshot.Build</c> assigns
/// <c>OrderItemIngredient.SortOrder = index</c> over whatever sequence
/// <c>OrderIngredientCustomizations.ProjectRecipe</c> returns, and that sequence arrived as
/// <c>Product.DetailedIngredients</c> — an EF <c>.Include(...)</c> with no <c>OrderBy</c> on ANY call
/// path. So <c>SortOrder</c> WAS populated and a given order always printed the same way; what was
/// undefined is which ingredient became index 0. Two receipts for the same dish could list the same
/// ingredients in different orders, and neither had to match <c>ProductIngredient.DisplayOrder</c> —
/// the order #603 shipped drag-reordering to control.
/// </para>
/// <para>
/// It also made <c>WaiterLineIngredientSelectionTests.An_ingredient_id_from_outside_the_recipe_is_inert</c>
/// fail by luck (backend#441): the same three ids in a different order, on a re-run of a commit that
/// had already passed.
/// </para>
/// <para>
/// <b>The fixture is deliberately hostile.</b> The recipe is INSERTED in reverse <c>DisplayOrder</c>,
/// so a fixture that happens to be already sorted cannot let this pass vacuously — the two orders
/// disagree, and only a real sort can satisfy both assertions. Two ingredients share a
/// <c>DisplayOrder</c> as well, because live data holds gaps AND duplicates and a tie is exactly where
/// the old behaviour hid.
/// </para>
/// <para>
/// Nothing already frozen is backfilled. Each existing row is faithful to what was rendered at
/// checkout, and a receipt records what happened rather than what we would prefer it had looked like.
/// </para>
/// <para>
/// <b>This test is NOT the control.</b> Measured: with the <c>OrderBy</c> reverted it still passed —
/// the recipe came back from Postgres already sorted on this fixture, and EF decides the INSERT order
/// within its own batch, so "inserted backwards" is a wish rather than a guarantee. It pins the
/// end-to-end path and the two-orders-agree property; the discriminating oracle is
/// <see cref="ProjectRecipeOrderTests"/> below, which is handed a shuffled list directly.
/// </para>
/// </remarks>
[Collection("Database Lane 1")]
public class OrderIngredientFreezeOrderTests : IntegrationTestBase
{
    public OrderIngredientFreezeOrderTests(DatabaseFixture databaseFixture)
        : base(databaseFixture)
    {
    }

    private static readonly Guid ProductId = Guid.NewGuid();

    // DisplayOrder 0,1,2,2 — the last pair TIES on purpose. Declared in the order a receipt must
    // render them; the fixture inserts them backwards.
    private static readonly Guid BunId = Guid.NewGuid();      // DisplayOrder 0
    private static readonly Guid PattyId = Guid.NewGuid();    // DisplayOrder 1
    private static readonly Guid CheeseId = Guid.NewGuid();   // DisplayOrder 2
    private static readonly Guid OnionId = Guid.NewGuid();    // DisplayOrder 2 as well

    [Fact]
    public async Task The_frozen_rows_follow_DisplayOrder_and_two_orders_of_the_same_dish_agree()
    {
        AuthenticateAsRole(UserRole.Server);

        (await PostLineAsync()).EnsureSuccessStatusCode();
        (await PostLineAsync()).EnsureSuccessStatusCode();

        var lines = await FrozenPerLineAsync();

        // The admin's order, with the tie broken by id so the two rows sharing DisplayOrder 2 cannot
        // swap between prints.
        var expected = ExpectedOrder();

        lines.Should().HaveCount(2, "two lines were posted, and a per-line assertion needs both");
        lines[0].Should().Equal(expected, "the freeze follows the recipe order the admin arranged");
        lines[1].Should().Equal(expected, "and follows it identically for a second order of the same dish");
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────────────────

    private Task<HttpResponseMessage> PostLineAsync() =>
        Client.PostAsJsonAsync("/api/orders", new
        {
            type = nameof(OrderType.DineIn),
            customerName = "Table 9",
            items = new[]
            {
                new
                {
                    productId = ProductId,
                    quantity = 1,
                    unitPrice = 12.00m,
                    // Every row named, so the projection freezes the whole recipe and the assertion
                    // is about ORDER rather than about which rows survive.
                    selectedIngredientIds = new[] { BunId, PattyId, CheeseId, OnionId },
                },
            },
        });

    /// <summary>
    /// One list per order line, each line's rows in its own <c>SortOrder</c>. Grouped by the LINE
    /// rather than flattened by timestamp: two orders posted in the same tick would tie on
    /// <c>CreatedAt</c>, and a tie-break is the very thing under test.
    /// </summary>
    private async Task<List<List<string>>> FrozenPerLineAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var itemIds = await context.OrderItems
            .OrderBy(item => item.CreatedAt)
            .ThenBy(item => item.Id)
            .Select(item => item.Id)
            .ToListAsync();

        var lines = new List<List<string>>();
        foreach (var itemId in itemIds)
        {
            lines.Add(await context.Set<OrderItemIngredient>()
                .Where(row => row.OrderItemId == itemId)
                .OrderBy(row => row.SortOrder)
                .Select(row => row.IngredientName)
                .ToListAsync());
        }

        return lines;
    }

    /// <summary>
    /// The expected names, derived from the SAME rule the code applies rather than restated by hand:
    /// a hand-written literal would have to guess how the DisplayOrder-2 tie resolves, and guessing
    /// it is what this test exists to stop.
    /// </summary>
    private static List<string> ExpectedOrder() =>
        new List<(Guid Id, string Name, int Order)>
        {
            (BunId, "Bun", 0),
            (PattyId, "Patty", 1),
            (CheeseId, "Cheese", 2),
            (OnionId, "Onion", 2),
        }
        .OrderBy(row => row.Order)
        .ThenBy(row => row.Id)
        .Select(row => row.Name)
        .ToList();

    protected override async Task SeedTestData()
    {
        await base.SeedTestData();

        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        context.Users.Add(new ApplicationUser
        {
            Id = Guid.Parse(Common.TestAuthHandler.StaffUserId),
            UserName = Common.TestAuthHandler.StaffUserName,
            NormalizedUserName = Common.TestAuthHandler.StaffUserName.ToUpperInvariant(),
            Email = Common.TestAuthHandler.StaffUserName,
            NormalizedEmail = Common.TestAuthHandler.StaffUserName.ToUpperInvariant(),
            EmailConfirmed = true,
            FirstName = "Waiter",
            LastName = "User",
            Role = UserRole.Server,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = nameof(OrderIngredientFreezeOrderTests),
            RefreshToken = string.Empty,
            SecurityStamp = Guid.NewGuid().ToString(),
        });

        var burger = new Product
        {
            Id = ProductId,
            Name = "Burger",
            BasePrice = 12.00m,
            Type = ProductType.MainItem,
            IsActive = true,
            IsAvailable = true,
            Ingredients = new List<string>(),
            Allergens = new List<string>(),
            CreatedAt = DateTime.UtcNow,
            CreatedBy = nameof(OrderIngredientFreezeOrderTests),
        };

        // INSERTED BACKWARDS on purpose — see the class remarks.
        burger.DetailedIngredients.Add(NewIngredient(OnionId, "Onion", 2));
        burger.DetailedIngredients.Add(NewIngredient(CheeseId, "Cheese", 2));
        burger.DetailedIngredients.Add(NewIngredient(PattyId, "Patty", 1));
        burger.DetailedIngredients.Add(NewIngredient(BunId, "Bun", 0));

        context.Products.Add(burger);
        await context.SaveChangesAsync();
    }

    private static ProductIngredient NewIngredient(Guid id, string name, int order) => new()
    {
        Id = id,
        ProductId = ProductId,
        Name = name,
        IsOptional = false,
        IsIncludedInBasePrice = false,
        IsActive = true,
        Price = 0m,
        MaxQuantity = 1,
        DisplayOrder = order,
        CreatedAt = DateTime.UtcNow,
        CreatedBy = nameof(OrderIngredientFreezeOrderTests),
    };
}

/// <summary>
/// The discriminating half of the freeze-order fix: <c>ProjectRecipe</c> itself, with no database in
/// the way.
/// </summary>
/// <remarks>
/// The integration test above pins the end-to-end path, but it CANNOT DISCRIMINATE and it was
/// measured doing so: with the `OrderBy` reverted, it still passed, because what a recipe
/// `.Include(...)` returns is up to Postgres and on this fixture it happened to come back sorted.
/// A test that passes with and without the fix proves nothing about the fix, so the oracle lives
/// here instead — this class is handed a deliberately SHUFFLED list, which is the one input the
/// database cannot quietly sort for us.
/// </remarks>
public class ProjectRecipeOrderTests
{
    private static readonly Guid BunId = Guid.NewGuid();
    private static readonly Guid PattyId = Guid.NewGuid();
    private static readonly Guid CheeseId = Guid.NewGuid();
    private static readonly Guid OnionId = Guid.NewGuid();

    [Fact]
    public void A_shuffled_recipe_is_projected_in_DisplayOrder_with_the_tie_broken_by_id()
    {
        // Reverse DisplayOrder, and the two 2s adjacent — live DisplayOrder holds gaps AND
        // duplicates, so a tie is not a contrived case.
        var recipe = new List<ProductIngredient>
        {
            NewIngredient(OnionId, "Onion", 2),
            NewIngredient(CheeseId, "Cheese", 2),
            NewIngredient(PattyId, "Patty", 1),
            NewIngredient(BunId, "Bun", 0),
        };

        var quantities = recipe.ToDictionary(row => row.Id, _ => 1);

        var projected = OrderIngredientCustomizations.ProjectRecipe(recipe, quantities);

        projected.Should().NotBeNull();
        projected!.Select(row => row.IngredientName).Should().Equal(
            ExpectedOrder(),
            "the receipt renders the recipe in the order the admin arranged, not in the order a row happened to arrive");
    }

    /// <summary>Derived from the rule, not restated by hand: the tie's resolution is not guessable.</summary>
    private static List<string> ExpectedOrder() =>
        new List<(Guid Id, string Name, int Order)>
        {
            (BunId, "Bun", 0),
            (PattyId, "Patty", 1),
            (CheeseId, "Cheese", 2),
            (OnionId, "Onion", 2),
        }
        .OrderBy(row => row.Order)
        .ThenBy(row => row.Id)
        .Select(row => row.Name)
        .ToList();

    private static ProductIngredient NewIngredient(Guid id, string name, int order) => new()
    {
        Id = id,
        Name = name,
        IsOptional = false,
        IsActive = true,
        MaxQuantity = 1,
        DisplayOrder = order,
        CreatedAt = DateTime.UtcNow,
        CreatedBy = nameof(ProjectRecipeOrderTests),
    };
}
