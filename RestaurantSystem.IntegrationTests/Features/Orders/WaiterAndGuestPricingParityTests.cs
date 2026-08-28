using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Basket.Dtos.Requests;
using RestaurantSystem.Api.Features.Orders.Commands.CreateOrderFromBasketCommand;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

/// <summary>
/// THE ORACLE: the same dish, customised the same way, in the same quantity, must cost the same
/// whether a guest checked it out or a waiter rang it in.
/// </summary>
/// <remarks>
/// <para>
/// <b>This file exists because #433 shipped a defect that 1800 green tests did not see, and one of
/// those tests actively PINNED IT AS CORRECT.</b> The server-priced waiter path added the ingredient
/// customization ONCE per line, while the guest path
/// (<c>BasketLineTotal.ForRoot</c>: <c>(UnitPrice + CustomizationPrice) * Quantity</c>) adds it PER
/// UNIT. The two agree at quantity 1 — which is what every money assertion in
/// <c>WaiterLineIngredientSelectionTests</c> used, bar one, and that one asserted the defective
/// figure with a comment explaining why it was right.
/// </para>
/// <para>
/// The lesson is about the KIND of assertion, not the arithmetic. Every test that failed to catch
/// this compared the server against a number the test author had computed by the same reasoning that
/// produced the code — so the test could only ever confirm the author. What was missing was an
/// oracle the author did not compute: a second, independently-implemented path through the same
/// business rule. Mutation testing does not help here either, because a test that pins a defect IS
/// red when you fix the code; that is exactly what makes it dangerous.
/// </para>
/// <para>
/// So nothing below asserts a currency amount. Each case asserts only that the two paths AGREE.
/// A future change that breaks the rule in both places at once will still pass — no test can close
/// that — but a change that breaks either one alone cannot hide.
/// </para>
/// </remarks>
[Collection("Database Lane 2")]
public class WaiterAndGuestPricingParityTests : IntegrationTestBase
{
    public WaiterAndGuestPricingParityTests(DatabaseFixture databaseFixture)
        : base(databaseFixture)
    {
    }

    private const decimal PizzaPrice = 18.00m;
    private const decimal SaucePrice = 1.50m;   // optional, INCLUDED in base -> removing it DEDUCTS
    private const decimal BaconPrice = 2.50m;   // paid add-on -> selecting it ADDS

    private static readonly Guid ProductId = Guid.NewGuid();
    private static readonly Guid CheeseId = Guid.NewGuid();   // required
    private static readonly Guid SauceId = Guid.NewGuid();
    private static readonly Guid BaconId = Guid.NewGuid();

    private readonly string _sessionId = Guid.NewGuid().ToString();

    /// <summary>
    /// A PAID EXTRA, across quantities. The defect ran in this direction as an UNDERCHARGE: at
    /// quantity 3 the waiter line billed one rasher of bacon where the guest line billed three.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task A_paid_extra_costs_the_same_from_the_till_as_from_the_basket(int quantity)
    {
        var selection = new[] { CheeseId, SauceId, BaconId };
        var quantities = new Dictionary<Guid, int> { [BaconId] = 1 };

        var guest = await GuestTotalAsync(quantity, selection, quantities);
        var waiter = await WaiterTotalAsync(quantity, selection, quantities);

        waiter.Should().Be(guest,
            "the same dish ordered the same way must cost the same however it was rung in");
    }

    /// <summary>
    /// A REMOVAL, across quantities. The defect ran the OTHER way here — the deduction for the
    /// included-in-base sauce was applied once instead of per unit, so the waiter line charged MORE
    /// than the identical guest line. A single-direction test would have missed half of it.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task A_removal_deducts_the_same_from_the_till_as_from_the_basket(int quantity)
    {
        var selection = new[] { CheeseId };   // sauce off, no bacon
        var quantities = new Dictionary<Guid, int>();

        var guest = await GuestTotalAsync(quantity, selection, quantities);
        var waiter = await WaiterTotalAsync(quantity, selection, quantities);

        waiter.Should().Be(guest, "a deduction is per dish, exactly as a surcharge is");
    }

    /// <summary>
    /// Both directions on ONE line, which is the realistic order and the case where two
    /// compensating errors could still net out. Quantity 2 is the smallest that can tell
    /// per-unit from per-line at all.
    /// </summary>
    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public async Task An_extra_and_a_removal_together_agree_across_both_paths(int quantity)
    {
        var selection = new[] { CheeseId, BaconId };   // sauce removed AND bacon added
        var quantities = new Dictionary<Guid, int> { [BaconId] = 2 };

        var guest = await GuestTotalAsync(quantity, selection, quantities);
        var waiter = await WaiterTotalAsync(quantity, selection, quantities);

        waiter.Should().Be(guest);
    }

    /// <summary>
    /// The CONTROL, and it is not optional. Every assertion above is also satisfied by two paths
    /// that are equally and identically wrong — including two that both return zero. This pins that
    /// the fixture actually moves money: the customised line must differ from the plain one, and by
    /// an amount that scales with quantity. Without it, "waiter == guest" is a tautology waiting to
    /// happen.
    /// </summary>
    [Fact]
    public async Task The_control_the_fixture_actually_prices_a_customization()
    {
        var plain = await WaiterTotalAsync(2, new[] { CheeseId, SauceId }, new Dictionary<Guid, int>());
        var withBacon = await WaiterTotalAsync(2, new[] { CheeseId, SauceId, BaconId },
            new Dictionary<Guid, int> { [BaconId] = 1 });

        plain.Should().Be(PizzaPrice * 2, "the base recipe at quantity 2 is twice the base price");
        withBacon.Should().Be(plain + (BaconPrice * 2),
            "two pizzas with bacon carry two rashers — this is the assertion #433 got wrong");
    }

    // ── The two paths ────────────────────────────────────────────────────────────────────────

    /// <summary>The guest: add to the persisted basket, then check out. The real producer chain.</summary>
    private async Task<decimal> GuestTotalAsync(
        int quantity, IReadOnlyList<Guid> selectedIngredients, Dictionary<Guid, int> ingredientQuantities)
    {
        AuthenticateAsUser();
        Client.DefaultRequestHeaders.Remove("X-Session-Id");
        Client.DefaultRequestHeaders.Add("X-Session-Id", _sessionId);

        var add = await PostAsJsonAsync("/api/basket/items", new AddToBasketDto
        {
            ProductId = ProductId,
            Quantity = quantity,
            SelectedIngredients = selectedIngredients.ToList(),
            IngredientQuantities = ingredientQuantities,
        });
        add.StatusCode.Should().Be(HttpStatusCode.OK, "the basket must accept the line the oracle rests on");

        var checkout = await PostAsJsonAsync("/api/orders/from-basket", new CreateOrderFromBasketCommand
        {
            Type = OrderType.DineIn,
            TableNumber = 7,
            CustomerName = "Guest",
        });
        checkout.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await ReadResponseAsync<ApiResponse<OrderDto>>(checkout);
        result!.Success.Should().BeTrue();
        return result.Data!.Total;
    }

    /// <summary>The waiter: one hand-built POST, carrying the same selection.</summary>
    private async Task<decimal> WaiterTotalAsync(
        int quantity, IReadOnlyList<Guid> selectedIngredients, Dictionary<Guid, int> ingredientQuantities)
    {
        AuthenticateAsRole(UserRole.Server);
        Client.DefaultRequestHeaders.Remove("X-Session-Id");

        var response = await Client.PostAsJsonAsync("/api/orders", new
        {
            type = nameof(OrderType.DineIn),
            customerName = "Table 7",
            tableNumber = 7,
            items = new[]
            {
                new
                {
                    productId = ProductId,
                    quantity,
                    // Deliberately absurd: the whole point is that the server ignores it and prices
                    // the line itself, so parity cannot be an artefact of the till's own arithmetic.
                    unitPrice = 999.00m,
                    selectedIngredientIds = selectedIngredients,
                    ingredientQuantities,
                }
            },
        });
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await ReadResponseAsync<ApiResponse<OrderDto>>(response);
        result!.Success.Should().BeTrue();
        return result.Data!.Total;
    }

    // ── Fixture ──────────────────────────────────────────────────────────────────────────────

    protected override async Task SeedTestData()
    {
        await base.SeedTestData();

        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        // The Server-role identity has no AspNetUsers row and POST /api/orders writes Order.UserId,
        // which FKs it — see the note on TestAuthHandler.StaffUserId.
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
            CreatedBy = nameof(WaiterAndGuestPricingParityTests),
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
            CreatedBy = nameof(WaiterAndGuestPricingParityTests),
        };

        pizza.DetailedIngredients.Add(NewIngredient(CheeseId, "Cheese", isOptional: false, order: 0));
        pizza.DetailedIngredients.Add(NewIngredient(
            SauceId, "Tomato Sauce", isOptional: true, order: 1, includedInBase: true, price: SaucePrice));
        pizza.DetailedIngredients.Add(NewIngredient(
            BaconId, "Extra Bacon", isOptional: true, order: 2, price: BaconPrice, maxQuantity: 3));

        context.Products.Add(pizza);
        await context.SaveChangesAsync();
    }

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
            CreatedBy = nameof(WaiterAndGuestPricingParityTests),
        };
}
