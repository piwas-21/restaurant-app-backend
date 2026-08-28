using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Products;

/// <summary>
/// Slice S5 of SHARED-MODIFIERS-AND-SAUCES-PLAN (D7–D9): <b>a sauce is a typed ingredient, not a
/// second entity.</b> Owner ruling, §7 Q3/Q4 (2026-08-27): <i>"sauces logics can be similar to
/// ingredients; admin can decide for max/min included/excluded"</i>.
/// </summary>
/// <remarks>
/// <para>
/// The shape was chosen for exactly ONE reason, so that reason is what these tests pin. Both
/// <c>OrderItem.IngredientQuantitiesJson</c> and <c>BasketItem.IngredientQuantitiesJson</c> are bare
/// <c>Guid -&gt; int</c> maps with no table name in them. A separate sauce table would make every
/// key in every one of those columns ambiguous across two tables — on live baskets AND on immutable
/// order history. A discriminator column changes nothing about what an id means, and the
/// byte-identical id-map test below is the assertion that says so, rather than the plan merely
/// claiming it.
/// </para>
/// <para>
/// <b>S5 adds no pricing behaviour.</b> Ingredient money keeps its single writer
/// (<c>BasketPricingService.CalculateIngredientCustomizationPrice</c>); the
/// <c>sauceIncludedFree</c> rule lands there in S6. These tests therefore assert that the three
/// numbers are STORED and round-trip, and assert nothing about a price.
/// </para>
/// <para>
/// <b>There is no tenant default.</b> A product that never mentions sauces keeps 0 / null / 0 —
/// nothing required, no group cap, nothing free — which is what every product had before the
/// migration ran. No "one free sauce" rule exists in code or configuration, by owner ruling.
/// </para>
/// </remarks>
[Collection("Database Lane 4")]
public class ProductSauceKindTests : IntegrationTestBase
{
    private Guid _productId;
    private Guid _categoryId;
    private Guid _cheeseId;
    private Guid _garlicSauceId;

    public ProductSauceKindTests(DatabaseFixture databaseFixture)
        : base(databaseFixture)
    {
    }

    protected override async Task SeedTestData()
    {
        await base.SeedTestData();

        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        _categoryId = (await context.Categories.OrderBy(c => c.Name).FirstAsync()).Id;

        var product = new Product
        {
            Id = Guid.NewGuid(),
            Name = "Sauce Kebab",
            BasePrice = 18m,
            Type = ProductType.MainItem,
            IsActive = true,
            IsAvailable = true,
            Ingredients = new List<string>(),
            Allergens = new List<string>(),
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test"
        };
        _productId = product.Id;
        product.ProductCategories.Add(new ProductCategory
        {
            ProductId = product.Id,
            CategoryId = _categoryId,
            IsPrimary = true,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test"
        });

        // Seeded WITHOUT touching Kind, deliberately: this is the shape every row on production has,
        // and what the migration must leave meaning exactly what it meant.
        _cheeseId = Guid.NewGuid();
        product.DetailedIngredients.Add(new ProductIngredient
        {
            Id = _cheeseId,
            ProductId = product.Id,
            Name = "Cheese",
            IsOptional = true,
            IsIncludedInBasePrice = true,
            Price = 1.5m,
            MaxQuantity = 2,
            IsActive = true,
            DisplayOrder = 0,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test"
        });

        _garlicSauceId = Guid.NewGuid();
        product.DetailedIngredients.Add(new ProductIngredient
        {
            Id = _garlicSauceId,
            ProductId = product.Id,
            Name = "Garlic Sauce",
            Kind = IngredientKind.Sauce,
            IsOptional = true,
            Price = 0.5m,
            MaxQuantity = 1,
            IsActive = true,
            DisplayOrder = 1,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test"
        });

        context.Products.Add(product);
        await context.SaveChangesAsync();
    }

    // ── The migration is additive, and that is the point ─────────────────────────────────────

    /// <summary>
    /// The whole safety claim of D8 in one test: a row written with no opinion about
    /// <c>Kind</c> — every row on production — is an ingredient, and NOTHING else about it moved.
    /// </summary>
    [Fact]
    public async Task ARowThatPredatesTheColumn_IsAnIngredientAndIsOtherwiseUntouched()
    {
        var cheese = await ReadIngredientAsync(_cheeseId);

        cheese.Kind.Should().Be(IngredientKind.Ingredient, "0 is the column default AND the enum's zero value");
        cheese.Name.Should().Be("Cheese");
        cheese.IsOptional.Should().BeTrue();
        cheese.IsIncludedInBasePrice.Should().BeTrue();
        cheese.Price.Should().Be(1.5m);
        cheese.MaxQuantity.Should().Be(2);
        cheese.IsActive.Should().BeTrue();
        cheese.DisplayOrder.Should().Be(0);
        cheese.GlobalIngredientId.Should().BeNull();
    }

    /// <summary>
    /// A product that has never heard of sauces keeps the neutral values. This is the assertion that
    /// there is NO tenant default: if anybody ever hard-codes "1 free sauce", this test fails.
    /// </summary>
    [Fact]
    public async Task AProductThatNeverMentionsSauces_KeepsTheNeutralGroupValues()
    {
        var product = await ReadProductAsync();

        product.SauceMin.Should().Be(0);
        product.SauceMax.Should().BeNull("null is NO group cap — which is exactly today's behaviour");
        product.SauceIncludedFree.Should().Be(0);
    }

    // ── The round trip ───────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ASauce_RoundTripsThroughPutAndGet()
    {
        AuthenticateAsAdmin();

        var response = await PutAsync(BuildPayload(
            string.Join(",",
                Ingredient(_cheeseId, "Cheese", IngredientKind.Ingredient),
                Ingredient(_garlicSauceId, "Garlic Sauce", IngredientKind.Sauce, displayOrder: 1))));

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        (await ReadIngredientAsync(_garlicSauceId)).Kind.Should().Be(IngredientKind.Sauce);
        (await ReadIngredientAsync(_cheeseId)).Kind.Should().Be(IngredientKind.Ingredient);

        var ingredients = await GetIngredientsAsync();
        ingredients.GetArrayLength().Should().Be(2);
        ingredients[0].GetProperty("kind").GetString().Should().Be("ingredient");
        ingredients[1].GetProperty("kind").GetString().Should().Be("sauce");
    }

    /// <summary>
    /// The editor does not have to render every field, but it must ECHO every field. A client that
    /// re-sends the server's own output verbatim must not silently demote a sauce.
    /// </summary>
    [Fact]
    public async Task EchoingBackWhatTheGetReturned_KeepsTheKind()
    {
        AuthenticateAsAdmin();

        var returned = await GetIngredientsAsync();
        var echoed = string.Join(",", returned.EnumerateArray().Select(row => row.GetRawText()));

        (await PutAsync(BuildPayload(echoed, name: "Sauce Kebab (renamed)")))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        (await ReadIngredientAsync(_garlicSauceId)).Kind.Should().Be(IngredientKind.Sauce);
    }

    /// <summary>
    /// An OLD client — every client that existed before this slice — sends no <c>kind</c> at all.
    /// It must keep creating ingredients, not fail and not guess.
    /// </summary>
    [Fact]
    public async Task APayloadWithNoKindAtAll_StillSaysIngredient()
    {
        AuthenticateAsAdmin();

        var response = await PutAsync(BuildPayload(Ingredient(_cheeseId, "Cheese", kind: null)));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ReadIngredientAsync(_cheeseId)).Kind.Should().Be(IngredientKind.Ingredient);
    }

    [Fact]
    public async Task TheThreeGroupNumbers_RoundTripThroughPutAndGet()
    {
        AuthenticateAsAdmin();

        var response = await PutAsync(BuildPayload(
            Ingredient(_garlicSauceId, "Garlic Sauce", IngredientKind.Sauce),
            sauceGroup: "\"sauceMin\": 1, \"sauceMax\": 3, \"sauceIncludedFree\": 1,"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var stored = await ReadProductAsync();
        stored.SauceMin.Should().Be(1);
        stored.SauceMax.Should().Be(3);
        stored.SauceIncludedFree.Should().Be(1);

        var dto = await GetProductAsync();
        dto.GetProperty("sauceMin").GetInt32().Should().Be(1);
        dto.GetProperty("sauceMax").GetInt32().Should().Be(3);
        dto.GetProperty("sauceIncludedFree").GetInt32().Should().Be(1);
    }

    // ── Validation ───────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("\"sauceMin\": 4, \"sauceMax\": 2,", "minimum number of sauces cannot exceed")]
    [InlineData("\"sauceMax\": 2, \"sauceIncludedFree\": 3,", "free sauces cannot exceed")]
    [InlineData("\"sauceMin\": -1,", "minimum number of sauces cannot be negative")]
    [InlineData("\"sauceMax\": -1,", "maximum number of sauces cannot be negative")]
    [InlineData("\"sauceIncludedFree\": -1,", "free sauces cannot be negative")]
    public async Task ANonsensicalGroupRule_IsRejectedWithItsOwnReason(string sauceGroup, string expected)
    {
        AuthenticateAsAdmin();

        var response = await PutAsync(BuildPayload(
            Ingredient(_garlicSauceId, "Garlic Sauce", IngredientKind.Sauce),
            sauceGroup: sauceGroup));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain(expected);
    }

    /// <summary>
    /// The counterpart to the rejections: a null maximum is NO cap, so neither cross-field clause
    /// applies to it. Without this test the obvious "fix" — making the maximum a non-nullable int
    /// where 0 means unlimited — would look free, and it would make "no sauces allowed" and
    /// "unlimited sauces" the same payload.
    /// </summary>
    [Fact]
    public async Task ANullMaximum_IsNoCapAndConstrainsNothing()
    {
        AuthenticateAsAdmin();

        var response = await PutAsync(BuildPayload(
            Ingredient(_garlicSauceId, "Garlic Sauce", IngredientKind.Sauce),
            sauceGroup: "\"sauceMin\": 2, \"sauceMax\": null, \"sauceIncludedFree\": 9,"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var stored = await ReadProductAsync();
        stored.SauceMax.Should().BeNull();
        stored.SauceIncludedFree.Should().Be(9);
    }

    // ── The reason this shape was chosen ─────────────────────────────────────────────────────

    /// <summary>
    /// <b>Zero JSON-column impact, proved rather than asserted in prose.</b> An order line's
    /// <c>IngredientQuantitiesJson</c> is captured, the product is then saved WITH sauces in the
    /// payload, and the stored string is compared byte for byte. Every id must also still resolve to
    /// a live row — a map that survives intact but no longer resolves is the S0 defect wearing a
    /// disguise.
    /// </summary>
    [Fact]
    public async Task SavingAProductWithSauces_LeavesTheOrderIdMapByteIdentical()
    {
        AuthenticateAsAdmin();

        var orderId = await SeedOrderWithIdMapAsync("S5-JSON");
        var before = await ReadIdMapJsonAsync(orderId);

        var response = await PutAsync(BuildPayload(
            string.Join(",",
                Ingredient(_cheeseId, "Cheese", IngredientKind.Ingredient),
                Ingredient(_garlicSauceId, "Garlic Sauce", IngredientKind.Sauce, displayOrder: 1),
                Ingredient(null, "Chilli Sauce", IngredientKind.Sauce, displayOrder: 2)),
            sauceGroup: "\"sauceMin\": 0, \"sauceMax\": 2, \"sauceIncludedFree\": 1,"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var after = await ReadIdMapJsonAsync(orderId);
        after.Should().Be(before, "an id keeps meaning the same thing — that is the whole reason a "
            + "discriminator was chosen over a second table");

        var liveIds = await LiveIngredientIdsAsync();
        foreach (var savedId in JsonSerializer.Deserialize<Dictionary<Guid, int>>(after)!.Keys)
        {
            liveIds.Should().Contain(savedId, "a surviving map that resolves nothing is the S0 defect again");
        }
    }

    /// <summary>
    /// A sauce reaches the immutable order snapshot (S1) exactly like any other ingredient: same
    /// writer, same rows, same order. Nothing in S5 gives a sauce its own checkout path.
    /// </summary>
    [Fact]
    public async Task TheOrderSnapshot_FreezesASauceLikeAnyOtherIngredient()
    {
        var orderId = await CheckoutAsync("S5-SNAPSHOT");

        var frozen = await FrozenRowsAsync(orderId);

        frozen.Should().HaveCount(2);
        frozen[0].IngredientName.Should().Be("Cheese");
        frozen[0].IngredientId.Should().Be(_cheeseId);
        frozen[1].IngredientName.Should().Be("Garlic Sauce");
        frozen[1].IngredientId.Should().Be(_garlicSauceId);
        frozen[1].Quantity.Should().Be(1);
        frozen[1].IsRemoved.Should().BeFalse();
    }

    // ── helpers ──────────────────────────────────────────────────────────────────────────────

    private Task<HttpResponseMessage> PutAsync(string json) =>
        Client.PutAsync(
            $"/api/Products/{_productId}",
            new StringContent(json, Encoding.UTF8, "application/json"));

    /// <summary>
    /// Raw JSON rather than an anonymous object, following ProductIngredientProvenanceTests: the
    /// PRESENCE of `kind` and of the three numbers is under test, and a serializer would decide that.
    /// </summary>
    private string BuildPayload(string ingredientsFragment, string name = "Sauce Kebab", string sauceGroup = "") => $$"""
    {
      "id": "{{_productId}}",
      "name": "{{name}}",
      "basePrice": 18,
      "isActive": true,
      "isAvailable": true,
      "isSpecial": false,
      "preparationTimeMinutes": 10,
      "type": "mainItem",
      "kitchenType": "none",
      "displayOrder": 0,
      {{sauceGroup}}
      "categoryIds": ["{{_categoryId}}"],
      "primaryCategoryId": "{{_categoryId}}",
      "detailedIngredients": [{{ingredientsFragment}}]
    }
    """;

    private static string Ingredient(Guid? id, string name, IngredientKind? kind, int displayOrder = 0) => $$"""
    {
      {{(id is null ? "" : $"\"id\": \"{id}\",")}}
      {{(kind is null ? "" : $"\"kind\": \"{(kind == IngredientKind.Sauce ? "sauce" : "ingredient")}\",")}}
      "name": "{{name}}",
      "isOptional": true,
      "price": 0.5,
      "isIncludedInBasePrice": false,
      "isActive": true,
      "displayOrder": {{displayOrder}},
      "maxQuantity": 1
    }
    """;

    private async Task<ProductIngredient> ReadIngredientAsync(Guid ingredientId)
    {
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await context.ProductIngredients.AsNoTracking().SingleAsync(i => i.Id == ingredientId);
    }

    private async Task<Product> ReadProductAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await context.Products.AsNoTracking().SingleAsync(p => p.Id == _productId);
    }

    private async Task<List<Guid>> LiveIngredientIdsAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await context.ProductIngredients
            .Where(i => i.ProductId == _productId)
            .Select(i => i.Id)
            .ToListAsync();
    }

    private async Task<JsonElement> GetProductAsync()
    {
        var response = await Client.GetAsync($"/api/Products/{_productId}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("data").Clone();
    }

    private async Task<JsonElement> GetIngredientsAsync() =>
        (await GetProductAsync()).GetProperty("detailedIngredients").Clone();

    /// <summary>A pre-existing order line: the bare id map, which is the column under protection.</summary>
    private async Task<Guid> SeedOrderWithIdMapAsync(string orderNumber)
    {
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var order = NewOrder(orderNumber);
        order.Items.Add(new OrderItem
        {
            Id = Guid.NewGuid(),
            OrderId = order.Id,
            ProductId = _productId,
            ProductName = "Sauce Kebab",
            Quantity = 1,
            UnitPrice = 18m,
            ItemTotal = 18m,
            IngredientQuantitiesJson = JsonSerializer.Serialize(new Dictionary<Guid, int>
            {
                [_cheeseId] = 1,
                [_garlicSauceId] = 1
            }),
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test"
        });

        context.Orders.Add(order);
        await context.SaveChangesAsync();
        return order.Id;
    }

    /// <summary>Checks out through the single real writer, exactly as OrderIngredientSnapshotTests does.</summary>
    private async Task<Guid> CheckoutAsync(string orderNumber)
    {
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var factory = scope.ServiceProvider.GetRequiredService<IOrderItemFactory>();

        var order = NewOrder(orderNumber);

        var error = await factory.AddItemAsync(
            order,
            new CreateOrderItemDto
            {
                ProductId = _productId,
                Quantity = 1,
                UnitPrice = 18m,
                IngredientQuantities = new Dictionary<Guid, int>
                {
                    [_cheeseId] = 1,
                    [_garlicSauceId] = 1
                }
            },
            itemsAreServerPriced: true,
            CancellationToken.None);

        error.Should().BeNull();

        context.Orders.Add(order);
        await context.SaveChangesAsync();
        return order.Id;
    }

    private Order NewOrder(string orderNumber) => new()
    {
        Id = Guid.NewGuid(),
        OrderNumber = orderNumber,
        Type = OrderType.DineIn,
        Status = OrderStatus.Pending,
        PaymentStatus = PaymentStatus.Pending,
        OrderDate = DateTime.UtcNow,
        CreatedAt = DateTime.UtcNow,
        CreatedBy = "test"
    };

    private async Task<string> ReadIdMapJsonAsync(Guid orderId)
    {
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        return await context.OrderItems
            .Where(item => item.OrderId == orderId)
            .Select(item => item.IngredientQuantitiesJson!)
            .SingleAsync();
    }

    private async Task<List<OrderItemIngredient>> FrozenRowsAsync(Guid orderId)
    {
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var itemIds = await context.OrderItems
            .Where(item => item.OrderId == orderId)
            .Select(item => item.Id)
            .ToListAsync();

        return await context.Set<OrderItemIngredient>()
            .Where(row => itemIds.Contains(row.OrderItemId))
            .OrderBy(row => row.SortOrder)
            .ToListAsync();
    }
}
