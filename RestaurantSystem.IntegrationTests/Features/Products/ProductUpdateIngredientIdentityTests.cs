using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Api.Features.Products.Dtos;
using RestaurantSystem.Api.Features.Products.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Infrastructure;
using System.Net;
using System.Text;
using System.Text.Json;

namespace RestaurantSystem.IntegrationTests.Features.Products;

// The ingredient half of `PUT /api/Products/{id}` used to remove every ProductIngredient row of the
// product and re-add each one, so each row was reborn under a NEW Guid on every save — even a save
// that changed nothing but the product name.
//
// That id is not private to the product. `OrderItem.IngredientQuantitiesJson` and
// `BasketItem.IngredientQuantitiesJson` are `{ ingredientId: quantity }` maps written at checkout,
// and `OrderMappingService.MapIngredientCustomizations` resolves them against the CURRENT recipe.
// Re-keying the recipe therefore erased the ingredient detail of every past order of that product:
// a ticket that said "NO Onions" simply stopped saying anything, because no surviving row carried
// the id the order line names.
//
// The fix diffs by id (ProductIngredientSynchronizer), mirroring the variation block that has
// always done so. These tests pin the four cases of that diff plus the regression that is the whole
// point of it — a past order's ingredient lines are BYTE-IDENTICAL across an unrelated product edit.
//
// Why the order side is asserted through IOrderMappingService rather than through the raw JSON
// column: the column is not what breaks. It is written once and never touched by a product save, so
// comparing it before and after would pass even with the defect present. What breaks is the
// RESOLUTION of those ids against the recipe, which is exactly what the mapper does and what the
// printer feed and the order screens read.
[Collection("Database Lane 3")]
public class ProductUpdateIngredientIdentityTests : IntegrationTestBase
{
    private Guid _productId;
    private Guid _categoryId;
    private Guid _orderId;
    private Guid _cheeseId;
    private Guid _sauceId;
    private Guid _olivesId;

    public ProductUpdateIngredientIdentityTests(DatabaseFixture databaseFixture)
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
            Name = "Margherita Pizza",
            BasePrice = 15m,
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

        _cheeseId = AddIngredient(product, "Cheese", isOptional: true, displayOrder: 0, includedInBase: true);
        _sauceId = AddIngredient(product, "Tomato Sauce", isOptional: false, displayOrder: 1, includedInBase: true);
        _olivesId = AddIngredient(product, "Olives", isOptional: true, displayOrder: 2, includedInBase: false);

        // A placed order whose single line customises all three ingredients: cheese removed,
        // sauce kept, olives doubled. This is the history the product edit must not disturb.
        var order = new Order
        {
            Id = Guid.NewGuid(),
            OrderNumber = "TEST-S0",
            Type = OrderType.DineIn,
            Status = OrderStatus.Confirmed,
            PaymentStatus = PaymentStatus.Pending,
            OrderDate = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test"
        };
        _orderId = order.Id;
        order.Items.Add(new OrderItem
        {
            Id = Guid.NewGuid(),
            OrderId = order.Id,
            ProductId = product.Id,
            ProductName = product.Name,
            Quantity = 1,
            UnitPrice = 15m,
            ItemTotal = 15m,
            IngredientQuantitiesJson = JsonSerializer.Serialize(new Dictionary<Guid, int>
            {
                [_cheeseId] = 0,
                [_sauceId] = 1,
                [_olivesId] = 2
            }),
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test"
        });

        context.Products.Add(product);
        context.Orders.Add(order);
        await context.SaveChangesAsync();
    }

    private static Guid AddIngredient(Product product, string name, bool isOptional, int displayOrder, bool includedInBase)
    {
        var id = Guid.NewGuid();
        product.DetailedIngredients.Add(new ProductIngredient
        {
            Id = id,
            ProductId = product.Id,
            Name = name,
            IsOptional = isOptional,
            IsIncludedInBasePrice = includedInBase,
            Price = isOptional ? 1.5m : 0m,
            MaxQuantity = 2,
            IsActive = true,
            DisplayOrder = displayOrder,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test"
        });
        return id;
    }

    // ---- helpers ------------------------------------------------------------------------------

    /// <summary>
    /// The order's ingredient lines exactly as every consumer sees them (order screens, printer
    /// feed), serialised so the assertion can be a single byte-for-byte comparison rather than a
    /// per-field one that could miss a field nobody thought to check.
    /// </summary>
    private async Task<string> ReadOrderIngredientLinesAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var mapper = scope.ServiceProvider.GetRequiredService<IOrderMappingService>();

        var order = await context.Orders
            .Include(o => o.Items)
                .ThenInclude(i => i.Product)
                    .ThenInclude(p => p!.DetailedIngredients)
            .FirstAsync(o => o.Id == _orderId);

        var dto = await mapper.MapToOrderDtoAsync(order);
        return JsonSerializer.Serialize(dto.Items.Single().IngredientCustomizations);
    }

    private async Task<List<(Guid Id, string Name, decimal Price, int DisplayOrder)>> ReadIngredientsAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        return await context.ProductIngredients
            .Where(i => i.ProductId == _productId)
            .OrderBy(i => i.DisplayOrder)
            .Select(i => new ValueTuple<Guid, string, decimal, int>(i.Id, i.Name, i.Price, i.DisplayOrder))
            .ToListAsync();
    }

    private Task<HttpResponseMessage> PutRawAsync(string json) =>
        Client.PutAsync(
            $"/api/Products/{_productId}",
            new StringContent(json, Encoding.UTF8, "application/json"));

    /// <summary>
    /// The product payload, with <paramref name="ingredientsFragment"/> spliced in verbatim — raw
    /// JSON because the presence, absence and exact VALUE of each `id` is the subject of every test
    /// here, and an anonymous object would let a helpful serializer decide any of that.
    /// </summary>
    private string BuildPayload(string ingredientsFragment, string name = "Margherita Pizza") => $$"""
    {
      "id": "{{_productId}}",
      "name": "{{name}}",
      "basePrice": 15,
      "isActive": true,
      "isAvailable": true,
      "isSpecial": false,
      "preparationTimeMinutes": 10,
      "type": "mainItem",
      "kitchenType": "none",
      "displayOrder": 0,
      "categoryIds": ["{{_categoryId}}"],
      "primaryCategoryId": "{{_categoryId}}",
      "detailedIngredients": [{{ingredientsFragment}}]
    }
    """;

    private string Ingredient(Guid? id, string name, decimal price, int displayOrder, bool isOptional, bool includedInBase) => $$"""
    {
      {{(id is null ? "" : $"\"id\": \"{id}\",")}}
      "name": "{{name}}",
      "isOptional": {{(isOptional ? "true" : "false")}},
      "price": {{price.ToString(System.Globalization.CultureInfo.InvariantCulture)}},
      "isIncludedInBasePrice": {{(includedInBase ? "true" : "false")}},
      "isActive": true,
      "displayOrder": {{displayOrder}},
      "maxQuantity": 2
    }
    """;

    /// <summary>The three seeded ingredients, sent back exactly as they are stored.</summary>
    private string AllThreeUnchanged() => string.Join(",",
        Ingredient(_cheeseId, "Cheese", 1.5m, 0, isOptional: true, includedInBase: true),
        Ingredient(_sauceId, "Tomato Sauce", 0m, 1, isOptional: false, includedInBase: true),
        Ingredient(_olivesId, "Olives", 1.5m, 2, isOptional: true, includedInBase: false));

    // ---- the regression this slice exists for -------------------------------------------------

    // Edit the product the way an admin does — rename it, leave the recipe alone — and the past
    // order must read back identically. Pre-fix this failed on the FIRST save: all three ids were
    // re-minted, so the mapper matched none of them and every customisation line vanished (the
    // required sauce reappearing only as a bare "removed" line under a new id).
    [Fact]
    public async Task ProductEdit_KeepsIngredientIds_SoAPastOrdersLinesAreByteIdentical()
    {
        AuthenticateAsAdmin();

        var before = await ReadOrderIngredientLinesAsync();
        before.Should().Contain("Olives", "the seeded order must actually carry ingredient detail");

        var response = await PutRawAsync(BuildPayload(AllThreeUnchanged(), name: "Margherita Pizza (renamed)"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        // The rename is the positive signal that the handler reached and completed its write path:
        // a refusal is also a 200 here (ApiResponse.Failure through Ok()), and would leave the
        // order untouched for the wrong reason.
        (await ReadIngredientsAsync()).Select(i => i.Id).Should().Equal(_cheeseId, _sauceId, _olivesId);
        (await ReadOrderIngredientLinesAsync()).Should().Be(before);
    }

    // The same product saved twice, which is the shape the defect took in production: every
    // successive save re-keyed the recipe again, so the damage was not a one-off.
    [Fact]
    public async Task RepeatedSaves_DoNotRekeyTheRecipe()
    {
        AuthenticateAsAdmin();

        var before = await ReadOrderIngredientLinesAsync();

        (await PutRawAsync(BuildPayload(AllThreeUnchanged()))).StatusCode.Should().Be(HttpStatusCode.OK);
        (await PutRawAsync(BuildPayload(AllThreeUnchanged()))).StatusCode.Should().Be(HttpStatusCode.OK);

        (await ReadOrderIngredientLinesAsync()).Should().Be(before);
    }

    // ---- the four cases of the diff -----------------------------------------------------------

    // An id supplied means UPDATE IN PLACE: the row keeps its id and takes the new values.
    [Fact]
    public async Task SuppliedId_UpdatesTheRowInPlace()
    {
        AuthenticateAsAdmin();

        var fragment = string.Join(",",
            Ingredient(_cheeseId, "Extra Cheese", 2.5m, 0, isOptional: true, includedInBase: true),
            Ingredient(_sauceId, "Tomato Sauce", 0m, 1, isOptional: false, includedInBase: true),
            Ingredient(_olivesId, "Olives", 1.5m, 2, isOptional: true, includedInBase: false));

        (await PutRawAsync(BuildPayload(fragment))).StatusCode.Should().Be(HttpStatusCode.OK);

        var ingredients = await ReadIngredientsAsync();
        ingredients.Should().Equal(
            (_cheeseId, "Extra Cheese", 2.5m, 0),
            (_sauceId, "Tomato Sauce", 0m, 1),
            (_olivesId, "Olives", 1.5m, 2));
    }

    // An ingredient the payload no longer mentions is REMOVED.
    [Fact]
    public async Task OmittedIngredient_IsDeleted()
    {
        AuthenticateAsAdmin();

        var fragment = string.Join(",",
            Ingredient(_cheeseId, "Cheese", 1.5m, 0, isOptional: true, includedInBase: true),
            Ingredient(_sauceId, "Tomato Sauce", 0m, 1, isOptional: false, includedInBase: true));

        (await PutRawAsync(BuildPayload(fragment))).StatusCode.Should().Be(HttpStatusCode.OK);

        (await ReadIngredientsAsync()).Select(i => i.Id).Should().Equal(_cheeseId, _sauceId);
    }

    // An entry with no id is CREATED — under a fresh id, leaving the existing ones alone.
    [Fact]
    public async Task IngredientWithoutId_IsCreated_AndTheOthersKeepTheirIds()
    {
        AuthenticateAsAdmin();

        var fragment = AllThreeUnchanged() + "," +
            Ingredient(null, "Basil", 0.5m, 3, isOptional: true, includedInBase: false);

        (await PutRawAsync(BuildPayload(fragment))).StatusCode.Should().Be(HttpStatusCode.OK);

        var ingredients = await ReadIngredientsAsync();
        ingredients.Select(i => i.Id).Take(3).Should().Equal(_cheeseId, _sauceId, _olivesId);
        ingredients.Should().HaveCount(4);
        ingredients[3].Name.Should().Be("Basil");
        ingredients[3].Id.Should().NotBe(Guid.Empty);
    }

    // An id that belongs to no ingredient of this product is SKIPPED. The load-bearing half of the
    // assertion is the second one: skipping must not take the rest of the recipe with it, and must
    // not mint a row under an id the caller chose.
    [Fact]
    public async Task UnknownIngredientId_IsSkipped_AndDeletesNothing()
    {
        AuthenticateAsAdmin();

        var strangerId = Guid.NewGuid();
        var fragment = AllThreeUnchanged() + "," +
            Ingredient(strangerId, "Not Ours", 9m, 3, isOptional: true, includedInBase: false);

        var before = await ReadOrderIngredientLinesAsync();

        (await PutRawAsync(BuildPayload(fragment))).StatusCode.Should().Be(HttpStatusCode.OK);

        var ingredients = await ReadIngredientsAsync();
        ingredients.Select(i => i.Id).Should().Equal(_cheeseId, _sauceId, _olivesId);
        ingredients.Should().NotContain(i => i.Name == "Not Ours");
        (await ReadOrderIngredientLinesAsync()).Should().Be(before);
    }

    // The warning that goes with that skip, asserted at the source rather than through HTTP: the
    // handler's logger is the host's, and capturing it would cost this class its shared host.
    [Fact]
    public async Task UnknownIngredientId_LogsAWarningNamingTheIdAndTheProduct()
    {
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var logger = new CapturingLogger();

        var product = await context.Products
            .Include(p => p.DetailedIngredients)
            .FirstAsync(p => p.Id == _productId);

        var strangerId = Guid.NewGuid();
        await ProductIngredientSynchronizer.SyncAsync(
            context,
            product,
            new[]
            {
                new ProductIngredientDto { Id = _cheeseId, Name = "Cheese", IsActive = true },
                new ProductIngredientDto { Id = _sauceId, Name = "Tomato Sauce", IsActive = true },
                new ProductIngredientDto { Id = _olivesId, Name = "Olives", IsActive = true },
                new ProductIngredientDto { Id = strangerId, Name = "Not Ours", IsActive = true }
            },
            "test",
            logger,
            CancellationToken.None);

        logger.Warnings.Should().ContainSingle()
            .Which.Should().Contain(strangerId.ToString()).And.Contain(_productId.ToString());
    }

    /// <summary>Collects the WARNING messages written to it; ignores every other level.</summary>
    private sealed class CapturingLogger : ILogger
    {
        public List<string> Warnings { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Warning)
            {
                Warnings.Add(formatter(state, exception));
            }
        }
    }

    // ---- translations, which the diff has to replace rather than duplicate --------------------

    // An updated row's descriptions are replaced wholesale, exactly as the variation block does.
    // The unique index on (ProductIngredientId, LanguageCode) makes this more than cosmetic: an
    // add-without-remove would violate it on the second save of the same translation, and the
    // remove-and-recreate code this replaces never met that index because every row was new.
    [Fact]
    public async Task ExistingIngredientContent_IsReplaced_NotDuplicated()
    {
        AuthenticateAsAdmin();

        var withContent = $$"""
        {
          "id": "{{_cheeseId}}",
          "name": "Cheese",
          "isOptional": true,
          "price": 1.5,
          "isIncludedInBasePrice": true,
          "isActive": true,
          "displayOrder": 0,
          "maxQuantity": 2,
          "content": {
            "en": { "name": "Cheese", "description": "Creamy" },
            "fr": { "name": "Fromage", "description": "Cremeux" }
          }
        }
        """;

        (await PutRawAsync(BuildPayload(withContent))).StatusCode.Should().Be(HttpStatusCode.OK);
        (await PutRawAsync(BuildPayload(withContent))).StatusCode.Should().Be(HttpStatusCode.OK);

        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var descriptions = await context.ProductIngredientDescriptions
            .Where(d => d.ProductIngredientId == _cheeseId)
            .OrderBy(d => d.LanguageCode)
            .Select(d => new ValueTuple<string, string>(d.LanguageCode, d.Name))
            .ToListAsync();

        descriptions.Should().Equal(("en", "Cheese"), ("fr", "Fromage"));
    }
}
