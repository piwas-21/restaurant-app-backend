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

// SHARED-MODIFIERS-AND-SAUCES-PLAN §9 (D13-D15): `ProductIngredient.ExclusionGroup`, the key that
// makes two ingredients mutually exclusive — "if one is selected, the other is deactivated".
//
// The field is a per-product grouping KEY on the row, never a group entity, so nothing about an
// ingredient's identity changes and `IngredientQuantitiesJson` is untouched (plan D8's argument).
// At-most-one is enforced by the CLIENT; the server refuses only the three shapes a sheet could not
// render honestly (mixed kinds, a member the guest cannot remove, two members pre-selected by the
// base recipe) — everything else, including a payload that selects both members, is allowed and
// simply charges for both, which overpays rather than underpays (D14).
//
// Every test asserts the stored column or the GET projection, never just the status code: a save
// that silently drops the key is also a 200, and the silent drop is the failure this slice must not
// have. That is not a hypothetical here — `globalIngredientId` was invisible for exactly that
// reason until S2, and the ingredient writer used to re-key every row on every save until S0.
[Collection("Database Lane 1")]
public class ProductIngredientExclusionGroupTests : IntegrationTestBase
{
    private Guid _productId;
    private Guid _categoryId;
    private Guid _rareId;
    private Guid _wellDoneId;

    public ProductIngredientExclusionGroupTests(DatabaseFixture databaseFixture)
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
            Name = "Exclusion Burger",
            BasePrice = 20m,
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

        // Two rows that mean the same slot — the owner's example shape, seeded UNGROUPED so a test
        // can prove the grouping arrives through the API rather than through the fixture.
        _rareId = AddIngredient(product, "Rare", 0);
        _wellDoneId = AddIngredient(product, "Well done", 1);

        context.Products.Add(product);
        await context.SaveChangesAsync();
    }

    private static Guid AddIngredient(Product product, string name, int displayOrder)
    {
        var id = Guid.NewGuid();
        product.DetailedIngredients.Add(new ProductIngredient
        {
            Id = id,
            ProductId = product.Id,
            Name = name,
            IsOptional = true,
            IsIncludedInBasePrice = false,
            Price = 1m,
            MaxQuantity = 1,
            IsActive = true,
            DisplayOrder = displayOrder,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test"
        });
        return id;
    }

    // ---- helpers ------------------------------------------------------------------------------

    private Task<HttpResponseMessage> PutRawAsync(string json) =>
        Client.PutAsync(
            $"/api/Products/{_productId}",
            new StringContent(json, Encoding.UTF8, "application/json"));

    /// <summary>The stored key, read off the column — the projections are themselves under test.</summary>
    private async Task<string?> ReadStoredGroupAsync(Guid ingredientId)
    {
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        return await context.ProductIngredients
            .Where(i => i.Id == ingredientId)
            .Select(i => i.ExclusionGroup)
            .SingleAsync();
    }

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
    /// Raw JSON rather than an anonymous object, following the provenance and identity suites: the
    /// PRESENCE and the exact value of `exclusionGroup` is the subject of every test here, and a
    /// helpful serializer would otherwise decide both.
    /// </summary>
    private string BuildPayload(string ingredientsFragment, string name = "Exclusion Burger") => $$"""
    {
      "id": "{{_productId}}",
      "name": "{{name}}",
      "basePrice": 20,
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

    private static string Ingredient(
        Guid? id,
        string name,
        string? exclusionGroup,
        bool isOptional = true,
        bool isIncludedInBasePrice = false,
        int kind = 0) => $$"""
    {
      {{(id is null ? "" : $"\"id\": \"{id}\",")}}
      {{(exclusionGroup is null ? "" : $"\"exclusionGroup\": {JsonSerializer.Serialize(exclusionGroup)},")}}
      "name": "{{name}}",
      "isOptional": {{(isOptional ? "true" : "false")}},
      "price": 1,
      "isIncludedInBasePrice": {{(isIncludedInBasePrice ? "true" : "false")}},
      "isActive": true,
      "displayOrder": 0,
      "maxQuantity": 1,
      "kind": {{kind}}
    }
    """;

    private string TwoDoneness(string? rareGroup, string? wellDoneGroup) => BuildPayload(string.Join(",",
        Ingredient(_rareId, "Rare", rareGroup),
        Ingredient(_wellDoneId, "Well done", wellDoneGroup)));

    // ---- the slice in one test ----------------------------------------------------------------

    [Fact]
    public async Task ExclusionGroup_SurvivesAPutAndAReGet()
    {
        AuthenticateAsAdmin();

        (await PutRawAsync(TwoDoneness("doneness", "doneness"))).StatusCode.Should().Be(HttpStatusCode.OK);

        (await ReadStoredGroupAsync(_rareId)).Should().Be("doneness");
        (await ReadStoredGroupAsync(_wellDoneId)).Should().Be("doneness");

        var ingredients = await GetIngredientsAsync();
        ingredients.EnumerateArray()
            .Select(i => i.GetProperty("exclusionGroup").GetString())
            .Should().AllBe("doneness");
    }

    // The question the parent asked in so many words: does the new field survive a save? The
    // ingredient writer used to hard-delete and re-create every row with a fresh Guid on every save
    // (fixed by S0, `ProductIngredientSynchronizer`), so a field that is only written on the CREATE
    // branch looks correct on the first save and is silently lost on the second.
    [Fact]
    public async Task EditingSomethingElse_KeepsTheGroupAndTheIngredientIds()
    {
        AuthenticateAsAdmin();

        (await PutRawAsync(TwoDoneness("doneness", "doneness"))).StatusCode.Should().Be(HttpStatusCode.OK);

        // Re-send the server's own output VERBATIM while renaming the product, exactly as an editor
        // that round-trips the DTO does.
        var returned = await GetIngredientsAsync();
        var echoed = string.Join(",", returned.EnumerateArray().Select(i => i.GetRawText()));

        (await PutRawAsync(BuildPayload(echoed, name: "Exclusion Burger (renamed)")))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        (await ReadStoredGroupAsync(_rareId)).Should().Be("doneness");
        (await ReadStoredGroupAsync(_wellDoneId)).Should().Be("doneness");
    }

    // A row created in the same save takes its group too — a new ingredient has no id, so it lands
    // on the synchronizer's OTHER branch, and the two branches are exactly how a field comes to be
    // written in one place and forgotten in the other.
    [Fact]
    public async Task ANewIngredient_IsCreatedInItsGroup()
    {
        AuthenticateAsAdmin();

        var fragment = string.Join(",",
            Ingredient(_rareId, "Rare", "doneness"),
            Ingredient(_wellDoneId, "Well done", "doneness"),
            Ingredient(null, "Blue", "doneness"));

        (await PutRawAsync(BuildPayload(fragment))).StatusCode.Should().Be(HttpStatusCode.OK);

        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var created = await context.ProductIngredients
            .SingleAsync(i => i.ProductId == _productId && i.Name == "Blue");

        created.ExclusionGroup.Should().Be("doneness");
    }

    // Clearing the field in the editor sends "", and storing that verbatim would put EVERY cleared
    // row of the product into one anonymous group — turning "no group" into a group that makes
    // unrelated ingredients exclude each other. It is normalised to null at both write paths.
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ABlankGroup_IsStoredAsNoGroupAtAll(string blank)
    {
        AuthenticateAsAdmin();

        (await PutRawAsync(TwoDoneness(blank, blank))).StatusCode.Should().Be(HttpStatusCode.OK);

        (await ReadStoredGroupAsync(_rareId)).Should().BeNull();
        (await ReadStoredGroupAsync(_wellDoneId)).Should().BeNull();
    }

    // Trimmed, so " doneness" and "doneness" are ONE group rather than two that look identical in
    // the editor and behave as if they were unrelated.
    [Fact]
    public async Task TheKeyIsTrimmed_SoLooksTheSameMeansIsTheSame()
    {
        AuthenticateAsAdmin();

        (await PutRawAsync(TwoDoneness(" doneness", "doneness  "))).StatusCode.Should().Be(HttpStatusCode.OK);

        (await ReadStoredGroupAsync(_rareId)).Should().Be("doneness");
        (await ReadStoredGroupAsync(_wellDoneId)).Should().Be("doneness");
    }

    // Dropping the field from the payload leaves the group, exactly as dropping `globalIngredientId`
    // clears the provenance: the row the admin sees is the one that decides.
    [Fact]
    public async Task OmittingTheGroup_TakesTheRowOutOfIt()
    {
        AuthenticateAsAdmin();

        (await PutRawAsync(TwoDoneness("doneness", "doneness"))).StatusCode.Should().Be(HttpStatusCode.OK);
        (await PutRawAsync(TwoDoneness(null, "doneness"))).StatusCode.Should().Be(HttpStatusCode.OK);

        (await ReadStoredGroupAsync(_rareId)).Should().BeNull();
        (await ReadStoredGroupAsync(_wellDoneId)).Should().Be("doneness");
    }

    // ---- the three refusals -------------------------------------------------------------------

    // Q9. Sauces render in their own guest section, so a group holding one sauce and one plain
    // ingredient would be split across two blocks and could not be drawn as one choice.
    [Fact]
    public async Task AGroupMixingASauceAndAnIngredient_IsRefused()
    {
        AuthenticateAsAdmin();

        var fragment = string.Join(",",
            Ingredient(_rareId, "Rare", "doneness"),
            Ingredient(_wellDoneId, "Garlic sauce", "doneness", kind: 1));

        (await PutRawAsync(BuildPayload(fragment))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadStoredGroupAsync(_rareId)).Should().BeNull();
    }

    // A non-optional row is a fixed part of the recipe with a disabled control, so it can never be
    // deselected — a group containing one would let the guest end up with two members selected and
    // no way back to one.
    [Fact]
    public async Task AGroupWithAMemberTheGuestCannotRemove_IsRefused()
    {
        AuthenticateAsAdmin();

        var fragment = string.Join(",",
            Ingredient(_rareId, "Rare", "doneness"),
            Ingredient(_wellDoneId, "Well done", "doneness", isOptional: false));

        (await PutRawAsync(BuildPayload(fragment))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // The sheet opens on the base recipe, so two included-in-base members would make the OPENING
    // state break the group's own rule before the guest touched anything. Refusing it here is what
    // lets the client enforce exclusivity on interaction only, and never re-price a sheet on open.
    [Fact]
    public async Task TwoMembersIncludedInTheBasePrice_AreRefused()
    {
        AuthenticateAsAdmin();

        var fragment = string.Join(",",
            Ingredient(_rareId, "Rare", "doneness", isIncludedInBasePrice: true),
            Ingredient(_wellDoneId, "Well done", "doneness", isIncludedInBasePrice: true));

        (await PutRawAsync(BuildPayload(fragment))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ...but ONE member may be included in the base price: that is the "the burger comes medium,
    // change it if you like" shape, and it is the whole point of allowing a default at all.
    [Fact]
    public async Task OneMemberIncludedInTheBasePrice_IsAccepted()
    {
        AuthenticateAsAdmin();

        var fragment = string.Join(",",
            Ingredient(_rareId, "Rare", "doneness", isIncludedInBasePrice: true),
            Ingredient(_wellDoneId, "Well done", "doneness"));

        (await PutRawAsync(BuildPayload(fragment))).StatusCode.Should().Be(HttpStatusCode.OK);
        (await ReadStoredGroupAsync(_rareId)).Should().Be("doneness");
    }

    // A group of one is LEGAL and is not an error: the client degrades it to an ordinary checkbox,
    // which is the honest answer to "nothing to be exclusive with", and refusing it would block an
    // admin halfway through building a group.
    [Fact]
    public async Task AGroupWithASingleMember_IsAccepted()
    {
        AuthenticateAsAdmin();

        (await PutRawAsync(TwoDoneness("doneness", null))).StatusCode.Should().Be(HttpStatusCode.OK);

        (await ReadStoredGroupAsync(_rareId)).Should().Be("doneness");
        (await ReadStoredGroupAsync(_wellDoneId)).Should().BeNull();
    }

    // The column is 40 characters. The refusal is at the door rather than a 500 from the database,
    // and the limit is the entity's own constant so the two layers cannot drift.
    [Fact]
    public async Task AKeyLongerThanTheColumn_IsRefused()
    {
        AuthenticateAsAdmin();

        var tooLong = new string('x', ProductIngredient.ExclusionGroupMaxLength + 1);

        (await PutRawAsync(TwoDoneness(tooLong, tooLong))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadStoredGroupAsync(_rareId)).Should().BeNull();
    }

    // Two DIFFERENT groups on one product is the case the sauce route cannot express at all (there
    // is one sauce group per product), and it is the reason this field exists rather than the owner
    // being told to set `sauceMax = 1`.
    [Fact]
    public async Task TwoDistinctGroupsOnOneProduct_AreBothStored()
    {
        AuthenticateAsAdmin();

        var fragment = string.Join(",",
            Ingredient(_rareId, "Rare", "doneness"),
            Ingredient(_wellDoneId, "Well done", "doneness"),
            Ingredient(null, "White bread", "bread"),
            Ingredient(null, "Wholemeal bread", "bread"));

        (await PutRawAsync(BuildPayload(fragment))).StatusCode.Should().Be(HttpStatusCode.OK);

        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var groups = await context.ProductIngredients
            .Where(i => i.ProductId == _productId && i.ExclusionGroup != null)
            .GroupBy(i => i.ExclusionGroup!)
            .Select(g => new { Key = g.Key, Count = g.Count() })
            .ToListAsync();

        groups.Should().BeEquivalentTo(new[]
        {
            new { Key = "doneness", Count = 2 },
            new { Key = "bread", Count = 2 }
        });
    }
}
