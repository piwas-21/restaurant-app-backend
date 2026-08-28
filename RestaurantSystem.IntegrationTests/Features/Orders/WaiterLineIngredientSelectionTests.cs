using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

/// <summary>
/// #430 — a waiter's line says WHAT was customised, and the server prices it.
/// </summary>
/// <remarks>
/// <para>
/// <b>What was actually wrong, which is not what the issue said.</b> The issue claimed a waiter line
/// was priced from the catalogue and so lost its extras. It is the opposite: <c>UserRole.Server</c>
/// is inside <c>ICurrentUserService.IsStaff</c>, the take-order screen sends a bearer, so
/// <c>OrderItemFactory.cs</c>'s <c>pricesAreTrusted</c> was TRUE and the declared price was honoured
/// in full. The CHF 13.00 in the report is the anonymous path.
/// </para>
/// <para>
/// The real defect was structural: the take-order screen posted a customization PRICE and no
/// customization. <c>CreateOrderItemDto</c> carried only <c>IngredientQuantities</c>, a bare
/// <c>Guid -&gt; int</c> map, and the screen sent none — so the frozen S1 snapshot
/// (<c>OrderIngredientSnapshot.Build</c>, which returns <c>[]</c> for a null map) was ALWAYS empty
/// for a waiter order, and the extras and removals reached the kitchen only as prose in the note.
/// The money was declared by the client and never computed by the one writer of ingredient money,
/// <c>BasketPricingService.CalculateIngredientCustomizationPrice</c>.
/// </para>
/// <para>
/// So these tests assert the two halves together, on purpose: a selection that produces a NON-EMPTY
/// snapshot but a client-declared total would still be the same defect wearing a snapshot.
/// </para>
/// </remarks>
[Collection("Database Lane 4")]
public class WaiterLineIngredientSelectionTests : IntegrationTestBase
{
    public WaiterLineIngredientSelectionTests(DatabaseFixture databaseFixture)
        : base(databaseFixture)
    {
    }

    private const decimal PizzaPrice = 18.00m;
    private const decimal SaucePrice = 1.50m;
    private const decimal BaconPrice = 2.50m;

    private static readonly Guid ProductId = Guid.NewGuid();
    private static readonly Guid BundleId = Guid.NewGuid();
    private static readonly Guid CheeseId = Guid.NewGuid();   // required
    private static readonly Guid SauceId = Guid.NewGuid();    // optional, INCLUDED in base, 1.50
    private static readonly Guid BaconId = Guid.NewGuid();    // optional add-on, 2.50, max 3

    private const decimal SaucePriceEach = 1.00m;
    private static readonly Guid SauceProductId = Guid.NewGuid();   // BasePrice 10.00, SauceIncludedFree 1
    private static readonly Guid GarlicSauceId = Guid.NewGuid();
    private static readonly Guid ChilliSauceId = Guid.NewGuid();

    // ── The defect ───────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// THE REGRESSION TEST. The line the report describes: sauce taken off, two rashers of bacon
    /// added. Both halves are asserted — the snapshot rows the kitchen ticket and the receipt
    /// render from, and the money.
    /// </summary>
    [Fact]
    public async Task A_waiter_line_with_extras_and_removals_freezes_them_and_is_priced_by_the_server()
    {
        AuthenticateAsRole(UserRole.Server);

        var response = await PostAsync(new
        {
            productId = ProductId,
            quantity = 1,
            // Deliberately dishonest, and deliberately in the waiter's OWN favour in one direction
            // and the house's in the other: neither may survive.
            unitPrice = 5.00m,
            customizationPrice = 99.00m,
            selectedIngredientIds = new[] { CheeseId, BaconId },
            ingredientQuantities = new Dictionary<Guid, int> { [BaconId] = 2 },
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var frozen = await FrozenRowsAsync();
        frozen.Should().HaveCount(3,
            "the line renders every recipe row — kept, removed and added alike; before #430 this was EMPTY");

        frozen[0].IngredientName.Should().Be("Cheese");
        frozen[0].Quantity.Should().Be(1);
        frozen[0].IsRemoved.Should().BeFalse();

        frozen[1].IngredientName.Should().Be("Tomato Sauce");
        frozen[1].Quantity.Should().Be(0);
        frozen[1].IsRemoved.Should().BeTrue(
            "an ingredient absent from the selection is one the guest asked to have taken off");

        frozen[2].IngredientName.Should().Be("Extra Bacon");
        frozen[2].Quantity.Should().Be(2);
        frozen[2].IsRemoved.Should().BeFalse();

        // 18.00 base − 1.50 for the sauce that is in the base price and was removed
        //             + 2 × 2.50 for the bacon = 21.50. Neither 5.00 nor 99.00 appears.
        var order = await SingleOrderAsync();
        order.Total.Should().Be(PizzaPrice - SaucePrice + (BaconPrice * 2),
            "a line that has just described itself ingredient by ingredient is one the server can price");
    }

    /// <summary>
    /// The half above that is easy to get right by accident. Asserted through the READ path the
    /// kitchen ticket and the receipt actually use, because "there are rows in a table" is not the
    /// claim — the claim is that the ticket says "NO Tomato Sauce".
    /// </summary>
    [Fact]
    public async Task The_kitchen_ticket_shows_the_waiters_removal()
    {
        AuthenticateAsRole(UserRole.Server);

        await PostAsync(new
        {
            productId = ProductId,
            quantity = 1,
            unitPrice = PizzaPrice,
            selectedIngredientIds = new[] { CheeseId },
        });

        var rendered = await RenderedIngredientsAsync();

        rendered.Should().NotBeNull("before #430 a waiter line carried nothing for the ticket to render");
        rendered!.Single(row => row.IngredientName == "Tomato Sauce").IsRemoved.Should().BeTrue();
        rendered.Single(row => row.IngredientName == "Cheese").IsRemoved.Should().BeFalse();
    }

    // ── The pricing rule and its edges ───────────────────────────────────────────────────────

    /// <summary>
    /// An EMPTY selection is a real answer — every optional ingredient off — and it is the answer
    /// that MOVES MONEY, so it must not be read as "said nothing" and fall back to the declared
    /// price. The trigger is therefore null-vs-not, never <c>Count</c>.
    /// </summary>
    [Fact]
    public async Task An_empty_selection_strips_the_dish_rather_than_saying_nothing()
    {
        AuthenticateAsRole(UserRole.Server);

        await PostAsync(new
        {
            productId = ProductId,
            quantity = 1,
            unitPrice = 5.00m,
            selectedIngredientIds = Array.Empty<Guid>(),
        });

        (await SingleOrderAsync()).Total.Should().Be(PizzaPrice - SaucePrice,
            "everything off deducts the included-in-base sauce, and 5.00 is still not a price the waiter may name");
    }

    /// <summary>
    /// The quantity clamp reached through the endpoint rather than through the pricing service's own
    /// unit tests. Bacon is capped at 3; asking for 99 buys 3.
    /// </summary>
    [Fact]
    public async Task A_quantity_beyond_the_ingredients_maximum_is_clamped()
    {
        AuthenticateAsRole(UserRole.Server);

        await PostAsync(new
        {
            productId = ProductId,
            quantity = 1,
            unitPrice = PizzaPrice,
            selectedIngredientIds = new[] { CheeseId, SauceId, BaconId },
            ingredientQuantities = new Dictionary<Guid, int> { [BaconId] = 99 },
        });

        (await SingleOrderAsync()).Total.Should().Be(PizzaPrice + (BaconPrice * 3));
    }

    /// <summary>
    /// An id that belongs to no recipe row buys nothing. The pricing walks THIS product's
    /// ingredients and asks whether each is selected — a foreign id is never asked about, so it is
    /// inert rather than merely harmless-looking.
    /// </summary>
    [Fact]
    public async Task An_ingredient_id_from_outside_the_recipe_is_inert()
    {
        AuthenticateAsRole(UserRole.Server);

        await PostAsync(new
        {
            productId = ProductId,
            quantity = 1,
            unitPrice = PizzaPrice,
            selectedIngredientIds = new[] { CheeseId, SauceId, Guid.NewGuid() },
            ingredientQuantities = new Dictionary<Guid, int> { [Guid.NewGuid()] = 50 },
        });

        (await SingleOrderAsync()).Total.Should().Be(PizzaPrice, "nothing was added and nothing was removed");
        (await FrozenRowsAsync()).Select(row => row.IngredientId)
            .Should().Equal(new[] { CheeseId, SauceId, BaconId }, "only real recipe rows are frozen");
    }

    /// <summary>
    /// The product's SAUCE ALLOWANCE reaches the waiter's line (S6/#429, plan D10).
    /// <para>
    /// This case exists because of a near-miss, and it is the kind a rebase hides.
    /// <c>ILineCustomizationBuilder.Build</c> gained <c>sauceIncludedFree</c> with a DEFAULT of 0
    /// while this branch was in flight. Omitting it compiles, passes every other test here, and
    /// prices a sauce-allowance product as though it had none — so the waiter path would have
    /// OVERCHARGED for sauces the dish includes, which is the very defect class #430 exists to fix.
    /// Only a product that HAS an allowance can tell the two apart.
    /// </para>
    /// </summary>
    [Fact]
    public async Task The_products_sauce_allowance_is_honoured_on_a_waiter_line()
    {
        AuthenticateAsRole(UserRole.Server);

        await PostAsync(new
        {
            productId = SauceProductId,
            quantity = 1,
            unitPrice = 99.00m,
            selectedIngredientIds = new[] { GarlicSauceId, ChilliSauceId },
        });

        // Two paid sauces at 1.00 each, one of them free by the product's allowance of 1.
        (await SingleOrderAsync()).Total.Should().Be(10.00m + SaucePriceEach,
            "a default of 0 for sauceIncludedFree would charge 2.00 here and look perfectly plausible");
    }

    // ── What the carve-out still covers ──────────────────────────────────────────────────────

    /// <summary>
    /// The line the server CANNOT price keeps the staff declaration, and this is the case #329
    /// protected deliberately. A bundle's real price lives in the menu definition, so repricing it
    /// from <c>Product.BasePrice</c> reproduces exactly the undercharge <c>OrderItemFactory</c>'s
    /// refusal guard exists to prevent.
    /// <para>
    /// Without this case, "the server prices what it can price" could be implemented as "the server
    /// prices everything with a selection", and every hand-built bundle would silently lose the
    /// difference. The selection is sent HERE too — it must be the composedness that decides, not
    /// the absence of a selection.
    /// </para>
    /// </summary>
    [Fact]
    public async Task A_staff_declared_bundle_price_survives_a_selection()
    {
        AuthenticateAsRole(UserRole.Server);

        await PostAsync(new
        {
            productId = BundleId,
            quantity = 1,
            unitPrice = 24.00m,
            selectedIngredientIds = new[] { CheeseId },
        });

        (await SingleOrderAsync()).Total.Should().Be(24.00m,
            "Product.BasePrice cannot express a bundle, so 8.00 would be an undercharge, not a correction");
    }

    /// <summary>
    /// A line that says nothing about its ingredients is untouched by #430 — the whole reason every
    /// anonymous request that exists today is byte-identical is that the trigger is a field no
    /// current caller sends.
    /// </summary>
    [Fact]
    public async Task A_line_without_a_selection_still_takes_the_staff_price()
    {
        AuthenticateAsRole(UserRole.Server);

        await PostAsync(new { productId = ProductId, quantity = 1, unitPrice = 9.50m });

        (await SingleOrderAsync()).Total.Should().Be(9.50m);
        (await FrozenRowsAsync()).Should().BeEmpty("no selection, nothing to freeze — exactly as before");
    }

    /// <summary>
    /// The new field is not a way in. An anonymous caller sending a selection is priced by the
    /// server for BOTH terms — which it already was for the unit price, and now is for the
    /// customization too. The point of the case is that the field cannot buy trust.
    /// </summary>
    [Fact]
    public async Task An_anonymous_caller_gains_nothing_by_sending_a_selection()
    {
        AuthenticateAsAnonymous();

        var response = await PostAsync(new
        {
            productId = ProductId,
            quantity = 1,
            unitPrice = 0.01m,
            customizationPrice = -50.00m,
            selectedIngredientIds = new[] { CheeseId, SauceId, BaconId },
            ingredientQuantities = new Dictionary<Guid, int> { [BaconId] = 1 },
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await SingleOrderAsync()).Total.Should().Be(PizzaPrice + BaconPrice,
            "the catalogue prices the food and the recipe prices the bacon; the body prices nothing");
    }

    /// <summary>
    /// The one thing this change makes newly REACHABLE for an anonymous caller, pinned with its
    /// bound rather than left to be discovered.
    /// <para>
    /// Deducting for a removed included-in-base ingredient is a genuine product rule (#304 — the
    /// guest paid for that cheese inside the base price), and until now an anonymous
    /// <c>POST /api/orders</c> could not reach it at all: <c>ResolveCustomizationPrice</c> pinned an
    /// untrusted caller's customization at 0. It can now, by sending an empty selection. What the
    /// case asserts is that this buys the RECIPE'S deduction and not a penny more — the request
    /// still names no price, and the identical lever has always existed through
    /// <c>POST /api/basket</c> followed by <c>/orders/from-basket</c>.
    /// </para>
    /// <para>
    /// The residual it does NOT close: a product whose included-in-base optional ingredients cost
    /// more in total than its own <c>BasePrice</c> would produce a NEGATIVE line total. That is left
    /// unclamped on purpose — <c>BasketToOrderTranslator</c> states the decision ("a NEGATIVE result
    /// is legitimate rather than something to clamp"), a per-line floor would break the
    /// <c>sum(order.Items.ItemTotal) == basket.SubTotal</c> reconciliation, and
    /// <c>OrderPricingService</c> already floors the ORDER total at zero. It is a catalogue
    /// misconfiguration, reachable through the basket before this change and not worsened by it.
    /// </para>
    /// </summary>
    [Fact]
    public async Task An_anonymous_empty_selection_buys_the_recipes_deduction_and_no_more()
    {
        AuthenticateAsAnonymous();

        await PostAsync(new
        {
            productId = ProductId,
            quantity = 2,
            unitPrice = 0.01m,
            selectedIngredientIds = Array.Empty<Guid>(),
        });

        // The deduction is per LINE, not per unit — 2 × 18.00 − 1.50, never 2 × (18.00 − 1.50).
        (await SingleOrderAsync()).Total.Should().Be((PizzaPrice * 2) - SaucePrice);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────────────────

    private Task<HttpResponseMessage> PostAsync(object item) =>
        Client.PostAsJsonAsync("/api/orders", new
        {
            type = nameof(OrderType.DineIn),
            customerName = "Table 4",
            items = new[] { item },
        });

    private async Task<Order> SingleOrderAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await context.Orders.AsNoTracking().SingleAsync();
    }

    private async Task<List<OrderItemIngredient>> FrozenRowsAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var itemIds = await context.OrderItems.Select(item => item.Id).ToListAsync();
        return await context.Set<OrderItemIngredient>()
            .Where(row => itemIds.Contains(row.OrderItemId))
            .OrderBy(row => row.SortOrder)
            .ToListAsync();
    }

    /// <summary>The rows the order screen, the receipt and the printer feed all render.</summary>
    private async Task<List<Api.Features.Orders.Dtos.OrderItemIngredientDto>?> RenderedIngredientsAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var mapper = scope.ServiceProvider
            .GetRequiredService<Api.Features.Orders.Services.IOrderMappingService>();

        var order = await context.Orders.Include(o => o.Items).SingleAsync();
        return (await mapper.MapToOrderDtoAsync(order)).Items.Single().IngredientCustomizations;
    }

    protected override async Task SeedTestData()
    {
        await base.SeedTestData();

        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        // TestAuthHandler.StaffUserId has no row: its own doc says "nothing that authenticates
        // through this handler requires the caller's own row to exist", and until this file that
        // was true. POST /api/orders writes Order.UserId, which FKs AspNetUsers, so a Server-role
        // POST answered 500 on fk_orders_asp_net_users_user_id. Seeded HERE rather than in
        // TestDataSeeder because that seeder is shared by ~130 test classes and a third user row
        // would silently move any assertion that counts users. See the note added to TestAuthHandler.
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
            CreatedBy = nameof(WaiterLineIngredientSelectionTests),
            RefreshToken = string.Empty,
            SecurityStamp = Guid.NewGuid().ToString(),
        });

        var pizza = new Product
        {
            Id = ProductId,
            Name = "Margherita",
            BasePrice = PizzaPrice,
            Type = ProductType.MainItem,
            IsActive = true,
            IsAvailable = true,
            Ingredients = new List<string>(),
            Allergens = new List<string>(),
            CreatedAt = DateTime.UtcNow,
            CreatedBy = nameof(WaiterLineIngredientSelectionTests),
        };

        pizza.DetailedIngredients.Add(NewIngredient(CheeseId, "Cheese", isOptional: false, order: 0));
        // Optional but paid for inside the base price: leaving it out is a genuine DEDUCTION.
        pizza.DetailedIngredients.Add(NewIngredient(
            SauceId, "Tomato Sauce", isOptional: true, order: 1, includedInBase: true, price: SaucePrice));
        // A paid add-on: only ever added, never "removed".
        pizza.DetailedIngredients.Add(NewIngredient(
            BaconId, "Extra Bacon", isOptional: true, order: 2, price: BaconPrice, maxQuantity: 3));

        var sauced = new Product
        {
            Id = SauceProductId,
            Name = "Kebab",
            BasePrice = 10.00m,
            Type = ProductType.MainItem,
            IsActive = true,
            IsAvailable = true,
            SauceIncludedFree = 1,
            Ingredients = new List<string>(),
            Allergens = new List<string>(),
            CreatedAt = DateTime.UtcNow,
            CreatedBy = nameof(WaiterLineIngredientSelectionTests),
        };
        sauced.DetailedIngredients.Add(NewSauce(GarlicSauceId, "Garlic Sauce", order: 0));
        sauced.DetailedIngredients.Add(NewSauce(ChilliSauceId, "Chilli Sauce", order: 1));

        context.Products.Add(pizza);
        context.Products.Add(sauced);
        context.Products.Add(new Product
        {
            Id = BundleId,
            Name = "Lunch Bundle",
            BasePrice = 8.00m,
            Type = ProductType.Menu,
            IsActive = true,
            IsAvailable = true,
            Ingredients = new List<string>(),
            Allergens = new List<string>(),
            CreatedAt = DateTime.UtcNow,
            CreatedBy = nameof(WaiterLineIngredientSelectionTests),
        });

        await context.SaveChangesAsync();
    }

    /// <summary>A paid sauce — <c>Kind = Sauce</c> is what the allowance keys on.</summary>
    private static ProductIngredient NewSauce(Guid id, string name, int order) => new()
    {
        Id = id,
        ProductId = SauceProductId,
        Name = name,
        Kind = IngredientKind.Sauce,
        IsOptional = true,
        IsIncludedInBasePrice = false,
        IsActive = true,
        Price = SaucePriceEach,
        MaxQuantity = 3,
        DisplayOrder = order,
        CreatedAt = DateTime.UtcNow,
        CreatedBy = nameof(WaiterLineIngredientSelectionTests),
    };

    private static ProductIngredient NewIngredient(
        Guid id,
        string name,
        bool isOptional,
        int order,
        bool includedInBase = false,
        decimal price = 0m,
        int maxQuantity = 1) => new()
        {
            Id = id,
            ProductId = ProductId,
            Name = name,
            IsOptional = isOptional,
            IsIncludedInBasePrice = includedInBase,
            IsActive = true,
            Price = price,
            MaxQuantity = maxQuantity,
            DisplayOrder = order,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = nameof(WaiterLineIngredientSelectionTests),
        };
}
