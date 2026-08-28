using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Infrastructure;
using System.Net;
using System.Text;
using System.Text.Json;

namespace RestaurantSystem.IntegrationTests.Features.Products;

// S2 provenance. `ProductIngredient.GlobalIngredientId` has been a real, indexed FK since the
// GlobalIngredients migration, and the 654-entry library it points at is seeded in nine languages —
// but `ProductIngredientDto` had NO such property, so the id an admin picker attached was dropped by
// the model binder on the way in and was absent from every projection on the way out. The link could
// therefore never be persisted by any client, which is why `grep GlobalIngredientId` found writes
// only inside this test project.
//
// What the link means here is COPY provenance, not shared identity (plan D3): the name and the
// translations were copied out of the library row, nothing reads that row afterwards, and editing it
// does not propagate. It exists so the next slices can measure real reuse and can turn propagation on
// against a snapshot-backed order history rather than against the live catalog.
//
// The tests assert the DATABASE column and the GET projection, never just the status code: a save
// that silently drops the id is also a 200, and that silent drop is the whole defect.
[Collection("Database Lane 1")]
public class ProductIngredientProvenanceTests : IntegrationTestBase
{
    private Guid _productId;
    private Guid _categoryId;
    private Guid _cheeseId;
    private Guid _globalMozzarellaId;
    private Guid _globalArchivedId;
    private Guid _globalDeletedId;

    public ProductIngredientProvenanceTests(DatabaseFixture databaseFixture)
        : base(databaseFixture)
    {
    }

    protected override async Task SeedTestData()
    {
        await base.SeedTestData();

        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        _categoryId = (await context.Categories.OrderBy(c => c.Name).FirstAsync()).Id;

        var mozzarella = new GlobalIngredient
        {
            Id = Guid.NewGuid(),
            DefaultName = "Mozzarella",
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test"
        };
        _globalMozzarellaId = mozzarella.Id;

        // A library row the admin archived AFTER attaching it (S3). Archiving is the reversible
        // state a DELETE now produces for a row a product uses: the row stays readable and keeps
        // serving that product, but it is off the shelf, so no NEW link may point at it.
        var archived = new GlobalIngredient
        {
            Id = Guid.NewGuid(),
            DefaultName = "Discontinued Pesto",
            IsActive = true,
            ArchivedAt = DateTime.UtcNow,
            ArchivedBy = "test",
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test"
        };
        _globalArchivedId = archived.Id;

        // The other way off the shelf, and the one that predates S3: a soft delete, hidden by the
        // global query filter. Both must be refused as a NEW link, by two different mechanisms.
        var deleted = new GlobalIngredient
        {
            Id = Guid.NewGuid(),
            DefaultName = "Deleted Pesto",
            IsActive = false,
            IsDeleted = true,
            DeletedAt = DateTime.UtcNow,
            DeletedBy = "test",
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test"
        };
        _globalDeletedId = deleted.Id;

        var product = new Product
        {
            Id = Guid.NewGuid(),
            Name = "Provenance Pizza",
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

        context.GlobalIngredients.AddRange(mozzarella, archived, deleted);
        context.Products.Add(product);
        await context.SaveChangesAsync();
    }

    // ---- helpers ------------------------------------------------------------------------------

    private Task<HttpResponseMessage> PutRawAsync(string json) =>
        Client.PutAsync(
            $"/api/Products/{_productId}",
            new StringContent(json, Encoding.UTF8, "application/json"));

    private Task<HttpResponseMessage> PostRawAsync(string json) =>
        Client.PostAsync("/api/Products", new StringContent(json, Encoding.UTF8, "application/json"));

    /// <summary>
    /// The persisted link, read straight off the column rather than through any projection — the
    /// projections are themselves under test here.
    /// </summary>
    private async Task<Guid?> ReadStoredLinkAsync(Guid ingredientId)
    {
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        return await context.ProductIngredients
            .Where(i => i.Id == ingredientId)
            .Select(i => i.GlobalIngredientId)
            .SingleAsync();
    }

    /// <summary>
    /// Point the seeded ingredient at a library row directly in the database — the state a product
    /// is in AFTER a successful pick, so a test can act on what happens to it next.
    /// </summary>
    private async Task LinkCheeseToAsync(Guid globalIngredientId)
    {
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var ingredient = await context.ProductIngredients.SingleAsync(i => i.Id == _cheeseId);
        ingredient.GlobalIngredientId = globalIngredientId;
        await context.SaveChangesAsync();
    }

    /// <summary>The `detailedIngredients` array exactly as `GET /api/Products/{id}` serialises it.</summary>
    private async Task<JsonElement> GetIngredientsAsync()
    {
        var response = await Client.GetAsync($"/api/Products/{_productId}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement
            .GetProperty("data")
            .GetProperty("detailedIngredients")
            .Clone();
    }

    /// <summary>
    /// Raw JSON, following ProductUpdateIngredientIdentityTests: the presence and the exact value of
    /// `globalIngredientId` is the subject of every test here, and an anonymous object would let a
    /// helpful serializer decide either of those.
    /// </summary>
    private string BuildPayload(string ingredientsFragment, string name = "Provenance Pizza") => $$"""
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

    private static string Ingredient(Guid? id, string name, Guid? globalIngredientId) => $$"""
    {
      {{(id is null ? "" : $"\"id\": \"{id}\",")}}
      {{(globalIngredientId is null ? "" : $"\"globalIngredientId\": \"{globalIngredientId}\",")}}
      "name": "{{name}}",
      "isOptional": true,
      "price": 1.5,
      "isIncludedInBasePrice": true,
      "isActive": true,
      "displayOrder": 0,
      "maxQuantity": 2
    }
    """;

    // ---- the defect this slice exists for -----------------------------------------------------

    // The whole slice in one test: attach a library row through the product API and it must be
    // readable again. Pre-fix this failed twice over — the DTO had no such property, so the id was
    // discarded on the way in, and no projection emitted one on the way out.
    [Fact]
    public async Task PickedGlobalIngredient_SurvivesAPutAndAReGet()
    {
        AuthenticateAsAdmin();

        var response = await PutRawAsync(BuildPayload(
            Ingredient(_cheeseId, "Mozzarella", _globalMozzarellaId)));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ReadStoredLinkAsync(_cheeseId)).Should().Be(_globalMozzarellaId);

        var ingredients = await GetIngredientsAsync();
        ingredients.GetArrayLength().Should().Be(1);
        ingredients[0].GetProperty("globalIngredientId").GetGuid().Should().Be(_globalMozzarellaId);
    }

    // The round trip that matters for the editor: the admin form does not display the link, so the
    // second save is whatever the GET handed the client. If the field did not survive that echo, one
    // unrelated edit — a price change — would erase the provenance of every ingredient on the page.
    [Fact]
    public async Task EchoingBackWhatTheGetReturned_KeepsTheProvenance()
    {
        AuthenticateAsAdmin();

        (await PutRawAsync(BuildPayload(Ingredient(_cheeseId, "Mozzarella", _globalMozzarellaId))))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        // Re-send the server's own ingredient output VERBATIM while editing something else on the
        // product, exactly as a client that round-trips the DTO does.
        var returned = await GetIngredientsAsync();
        var echoed = returned[0].GetRawText();

        (await PutRawAsync(BuildPayload(echoed, name: "Provenance Pizza (renamed)")))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        (await ReadStoredLinkAsync(_cheeseId)).Should().Be(_globalMozzarellaId);
    }

    // A NEW ingredient — no id yet, which is every ingredient a picker adds — takes the link too.
    [Fact]
    public async Task NewIngredientFromTheLibrary_IsCreatedWithItsProvenance()
    {
        AuthenticateAsAdmin();

        var fragment = string.Join(",",
            Ingredient(_cheeseId, "Cheese", null),
            Ingredient(null, "Mozzarella", _globalMozzarellaId));

        (await PutRawAsync(BuildPayload(fragment))).StatusCode.Should().Be(HttpStatusCode.OK);

        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var created = await context.ProductIngredients
            .SingleAsync(i => i.ProductId == _productId && i.Name == "Mozzarella");

        created.Id.Should().NotBe(_cheeseId);
        created.GlobalIngredientId.Should().Be(_globalMozzarellaId);
        (await ReadStoredLinkAsync(_cheeseId)).Should().BeNull();
    }

    // Provenance is assigned from the payload, not merged into it: an ingredient the admin retyped
    // by hand is no longer a copy of the library row, and must stop claiming to be one.
    [Fact]
    public async Task DroppingTheLinkFromThePayload_ClearsTheProvenance()
    {
        AuthenticateAsAdmin();

        (await PutRawAsync(BuildPayload(Ingredient(_cheeseId, "Mozzarella", _globalMozzarellaId))))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        (await PutRawAsync(BuildPayload(Ingredient(_cheeseId, "House Cheese", null))))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        (await ReadStoredLinkAsync(_cheeseId)).Should().BeNull();
    }

    // `global_ingredient_id` is a FK with NO ACTION, so an id the caller invented would reach the
    // database as a 500 about a constraint — for what is an optional decoration on an otherwise
    // valid save. It is dropped with a warning instead, and the ingredient still saves.
    [Fact]
    public async Task UnknownGlobalIngredientId_IsDropped_AndTheIngredientStillSaves()
    {
        AuthenticateAsAdmin();

        var response = await PutRawAsync(BuildPayload(
            Ingredient(_cheeseId, "Mystery Cheese", Guid.NewGuid())));

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var ingredient = await context.ProductIngredients.SingleAsync(i => i.Id == _cheeseId);

        ingredient.Name.Should().Be("Mystery Cheese");
        ingredient.GlobalIngredientId.Should().BeNull();
    }

    // An archived row is not on the shelf any more, and the picker does not list it, so a NEW link
    // to one is treated exactly like an unknown id: dropped, not obeyed.
    [Fact]
    public async Task ArchivedGlobalIngredient_CannotBeNewlyAttached()
    {
        AuthenticateAsAdmin();

        var response = await PutRawAsync(BuildPayload(
            Ingredient(_cheeseId, "Pesto", _globalArchivedId)));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ReadStoredLinkAsync(_cheeseId)).Should().BeNull();
    }

    // The same for the other off-the-shelf state, which the global query filter hides rather than a
    // predicate: a soft-deleted row is not a library row any more either.
    [Fact]
    public async Task SoftDeletedGlobalIngredient_CannotBeNewlyAttached()
    {
        AuthenticateAsAdmin();

        var response = await PutRawAsync(BuildPayload(
            Ingredient(_cheeseId, "Pesto", _globalDeletedId)));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ReadStoredLinkAsync(_cheeseId)).Should().BeNull();
    }

    // …but a link that is ALREADY stored survives the row being archived. Only a CHANGED link is
    // checked, so archiving one library entry cannot silently erase the provenance of every
    // product that ever used it on that product's next save.
    [Fact]
    public async Task AnAlreadyStoredLink_SurvivesTheLibraryRowBeingArchived()
    {
        AuthenticateAsAdmin();
        await LinkCheeseToAsync(_globalArchivedId);

        var response = await PutRawAsync(BuildPayload(
            Ingredient(_cheeseId, "Pesto", _globalArchivedId)));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ReadStoredLinkAsync(_cheeseId)).Should().Be(_globalArchivedId);
    }

    // The create path is a second, separate writer — the synchronizer only serves PUT — so it gets
    // its own assertion rather than an assumption.
    [Fact]
    public async Task CreateProduct_PersistsTheProvenanceOfItsIngredients()
    {
        AuthenticateAsAdmin();

        var payload = $$"""
        {
          "name": "Created Provenance Pizza",
          "basePrice": 12,
          "isActive": true,
          "isAvailable": true,
          "isSpecial": false,
          "preparationTimeMinutes": 10,
          "type": "mainItem",
          "kitchenType": "none",
          "displayOrder": 0,
          "categoryIds": ["{{_categoryId}}"],
          "primaryCategoryId": "{{_categoryId}}",
          "content": { "en": { "name": "Created Provenance Pizza", "description": "d" } },
          "detailedIngredients": [{{Ingredient(null, "Mozzarella", _globalMozzarellaId)}}]
        }
        """;

        (await PostRawAsync(payload)).StatusCode.Should().Be(HttpStatusCode.OK);

        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var created = await context.ProductIngredients
            .SingleAsync(i => i.Product.Name == "Created Provenance Pizza");

        created.GlobalIngredientId.Should().Be(_globalMozzarellaId);
    }
}
