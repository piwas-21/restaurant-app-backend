using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RestaurantSystem.Api.Common.Validation;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Infrastructure;
using System.Net;
using System.Text;
using System.Text.Json;

namespace RestaurantSystem.IntegrationTests.Features.Products;

// Issue #316: #306 validated the product's TOP-LEVEL translation map; the two NESTED maps in the same
// two handlers — `variations[].content` and `detailedIngredients[].content` — were left unvalidated,
// and they fail harder. `ProductDescription` has no IEntityTypeConfiguration, so its columns are
// unbounded `text`; the nested description entities are configured with `varchar(10)` language codes
// and a `varchar(200)` ingredient name, so these maps add LENGTH-OVERFLOW 500s the fixed map cannot
// produce.
//
// SIX SHAPES WERE MEASURED THROUGH THIS ENDPOINT before the fix, with #306 already in place. Four
// answered 500 (a §5.4 violation — user-facing errors must be BadRequestException) and **two answered
// 200 and persisted a junk row**. That second pair is why every test here asserts the ROW IS ABSENT
// rather than asserting the status: a status-only test reads those two as success.
//
// Raw JSON throughout, following ProductUpdateContentTests: `"en": null` and a blank key cannot be
// expressed as a C# object graph, and those are exactly the shapes under test.
public class NestedContentValidationTests : IntegrationTestBase
{
    private Guid _productId;
    private Guid _categoryId;

    public NestedContentValidationTests(DatabaseFixture databaseFixture)
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
            Name = "Nested Content Product",
            BasePrice = 12m,
            Type = ProductType.MainItem,
            IsActive = true,
            IsAvailable = true,
            Ingredients = new List<string>(),
            Allergens = new List<string>(),
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test"
        };
        _productId = product.Id;

        context.Products.Add(product);
        await context.SaveChangesAsync();
    }

    private Task<HttpResponseMessage> PutRawAsync(string json) =>
        Client.PutAsync(
            $"/api/Products/{_productId}",
            new StringContent(json, Encoding.UTF8, "application/json"));

    private string BuildPayload(string nestedFragment) => $$"""
    {
      "id": "{{_productId}}",
      "name": "Nested Content Product",
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
      {{nestedFragment}}
    }
    """;

    private static string Ingredient(string contentFragment) => $$"""
    "detailedIngredients": [{
      "name": "Cheese",
      "isOptional": true,
      "price": 1.5,
      "isIncludedInBasePrice": false,
      "isActive": true,
      "displayOrder": 1,
      "maxQuantity": 3,
      "content": {{contentFragment}}
    }]
    """;

    private static string Variation(string contentFragment) => $$"""
    "variations": [{
      "name": "Large",
      "priceModifier": 2,
      "isActive": true,
      "displayOrder": 1,
      "content": {{contentFragment}}
    }]
    """;

    /// <summary>
    /// Every nested description row in the database, across BOTH tables. Read rather than counted so
    /// a failure names what leaked. This is the assertion the issue asks for: two of the six shapes
    /// used to answer 200 and write a row, so "the request was refused" is not the same claim as
    /// "nothing was persisted".
    /// </summary>
    private async Task<List<string>> ReadNestedRowsAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var ingredients = await context.ProductIngredientDescriptions
            .Select(d => "ingredient:" + d.LanguageCode + ":" + d.Name)
            .ToListAsync();
        var variations = await context.ProductVariationDescriptions
            .Select(d => "variation:" + d.LanguageCode + ":" + d.Name)
            .ToListAsync();

        return ingredients.Concat(variations).ToList();
    }

    /// <summary>
    /// The envelope's <c>errors</c> array, decoded. <c>ApiResponse.Failure</c> leaves the reason in
    /// <c>errors[0]</c> and keeps a generic <c>message</c> on some paths, so the array is the reliable
    /// place to read a refusal's reason.
    /// </summary>
    private static async Task<List<string>> ReadErrorsAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Array
            ? errors.EnumerateArray().Select(e => e.GetString() ?? "").ToList()
            : [];
    }

    public static TheoryData<string, string, string> MalformedNestedContent() => new()
    {
        // --- the four that used to be 500s ---
        { "ingredient-null-name", "ingredient", NestedContentRule.NameRequiredMessage },
        { "ingredient-null-entry", "ingredient", NestedContentRule.EntryRequiredMessage },
        { "ingredient-oversize-key", "ingredient", "is longer than 10 characters" },
        { "variation-null-entry", "variation", NestedContentRule.EntryRequiredMessage },
        // --- the two that used to be 200 WITH a persisted junk row ---
        { "ingredient-blank-key", "ingredient", NestedContentRule.LanguageKeyRequiredMessage },
        { "variation-blank-key", "variation", NestedContentRule.LanguageKeyRequiredMessage },
    };

    [Theory]
    [MemberData(nameof(MalformedNestedContent))]
    public async Task MalformedNestedContent_IsRefused_AndPersistsNothing(
        string shape, string kind, string expectedMessage)
    {
        AuthenticateAsAdmin();

        var content = shape switch
        {
            "ingredient-null-name" => """{"en": {"description": "d"}}""",
            "ingredient-null-entry" => """{"en": null}""",
            "ingredient-oversize-key" => """{"averyverylonglanguagecode": {"name": "n"}}""",
            "ingredient-blank-key" => """{"": {"name": "n"}}""",
            "variation-null-entry" => """{"en": null}""",
            "variation-blank-key" => """{"": {"name": "n", "description": "d"}}""",
            _ => throw new ArgumentOutOfRangeException(nameof(shape))
        };

        var fragment = kind == "ingredient" ? Ingredient(content) : Variation(content);
        var response = await PutRawAsync(BuildPayload(fragment));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            $"'{shape}' is a malformed request, not a server fault (CLAUDE.md §5.4)");

        // WHICH 400 — the assertion that stops this test passing for the wrong reason. A payload
        // rejected by model binding also answers 400, and would do so with the rule deleted, so a
        // status-only check cannot tell "FluentValidation refused it" from "it never reached the
        // pipeline". The rule's own message in the envelope is what distinguishes them.
        //
        // Read from the DECODED `errors` array rather than by substring on the raw body: these
        // messages contain an apostrophe, which the serializer escapes to \u0027, so a raw-body
        // Contain fails against a response that is in fact correct.
        (await ReadErrorsAsync(response)).Should().Contain(e => e.Contains(expectedMessage, StringComparison.Ordinal),
            $"'{shape}' must be refused by NestedContentRule, not by model binding");

        // The half a status-code assertion cannot make. `ingredient-blank-key` and
        // `variation-blank-key` both answered 200 here and wrote a row whose language code is '' —
        // a row no locale will ever match, and which a status-only test calls a success.
        (await ReadNestedRowsAsync()).Should().BeEmpty(
            $"'{shape}' must not persist a translation row");
    }

    // ---- One test per COLUMN BOUND, because a bound read wrong is invisible ---------------------
    //
    // Each of these is a real column limit, and each was a live 500 at some point in this fix. The
    // variation name is here because an earlier version of the rule documented that column as
    // UNBOUNDED and passed no limit for it — the tests all passed, because none of them sent a long
    // variation name. A bound asserted only in a comment is not a bound.
    [Theory]
    [InlineData("ingredient-name", 201)]
    [InlineData("variation-name", 101)]
    [InlineData("ingredient-description", 501)]
    [InlineData("variation-description", 501)]
    public async Task AValueLongerThanItsColumn_IsRefused_AndPersistsNothing(string field, int length)
    {
        AuthenticateAsAdmin();

        var tooLong = new string('x', length);
        var (fragment, expected) = field switch
        {
            "ingredient-name" => (Ingredient($$"""{"en": {"name": "{{tooLong}}"} }"""),
                NestedContentRule.NameTooLongMessage("en", NestedContentRule.IngredientNameMaxLength)),
            "variation-name" => (Variation($$"""{"en": {"name": "{{tooLong}}"} }"""),
                NestedContentRule.NameTooLongMessage("en", NestedContentRule.VariationNameMaxLength)),
            "ingredient-description" => (Ingredient($$"""{"en": {"name": "n", "description": "{{tooLong}}"} }"""),
                NestedContentRule.DescriptionTooLongMessage("en")),
            "variation-description" => (Variation($$"""{"en": {"name": "n", "description": "{{tooLong}}"} }"""),
                NestedContentRule.DescriptionTooLongMessage("en")),
            _ => throw new ArgumentOutOfRangeException(nameof(field))
        };

        var response = await PutRawAsync(BuildPayload(fragment));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest, $"'{field}' overflows its column");
        (await ReadErrorsAsync(response)).Should().Contain(e => e.Contains(expected, StringComparison.Ordinal));
        (await ReadNestedRowsAsync()).Should().BeEmpty();
    }

    // A value AT its limit must still be accepted — the direction a bound gets wrong silently. An
    // off-by-one here would reject legal admin saves and nothing else in the suite would notice.
    [Theory]
    [InlineData("variation-name", 100)]
    [InlineData("ingredient-name", 200)]
    public async Task AValueExactlyAtItsColumnLimit_IsAccepted(string field, int length)
    {
        AuthenticateAsAdmin();

        var atLimit = new string('x', length);
        var fragment = field == "variation-name"
            ? Variation($$"""{"en": {"name": "{{atLimit}}"} }""")
            : Ingredient($$"""{"en": {"name": "{{atLimit}}"} }""");

        (await PutRawAsync(BuildPayload(fragment))).StatusCode.Should().Be(HttpStatusCode.OK);
        (await ReadNestedRowsAsync()).Should().ContainSingle();
    }

    // The over-strictness guard. These maps are ordinary payloads from the admin editor, and a rule
    // that refused them would break every product save that carries a translated ingredient — a
    // bigger defect than the one being fixed. A null DESCRIPTION is deliberately included: both
    // nested content DTOs declare it nullable and both columns accept null, unlike the top-level map.
    [Fact]
    public async Task WellFormedNestedContent_IsAccepted_AndPersisted()
    {
        AuthenticateAsAdmin();

        var fragment = Ingredient("""{"en": {"name": "Cheese", "description": null}}""")
            + ",\n" + Variation("""{"en": {"name": "Large", "description": "Big one"}}""");

        var response = await PutRawAsync(BuildPayload(fragment));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ReadNestedRowsAsync()).Should().BeEquivalentTo(
            ["ingredient:en:Cheese", "variation:en:Large"]);
    }

    // An absent nested `content` key means "no translations for this item" and must stay a no-op —
    // the same contract the update path's top-level map has (#190), and the reason the rule passes a
    // null map instead of requiring one.
    [Fact]
    public async Task AnAbsentNestedContentKey_IsAccepted()
    {
        AuthenticateAsAdmin();

        var fragment = """
        "detailedIngredients": [{
          "name": "Cheese",
          "isOptional": true,
          "price": 1.5,
          "isIncludedInBasePrice": false,
          "isActive": true,
          "displayOrder": 1,
          "maxQuantity": 3
        }]
        """;

        var response = await PutRawAsync(BuildPayload(fragment));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ReadNestedRowsAsync()).Should().BeEmpty();
    }
}
