using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Infrastructure;
using System.Net;
using System.Text;

namespace RestaurantSystem.IntegrationTests.Features.Products;

/// <summary>
/// The three defects the backend analysis attached to slice S4 (§9). None of them is about the new
/// library; all three are about variations having been the least-guarded part of the product write
/// path, and each is asserted against the behaviour a user or a deploy would meet — a status code, a
/// database error, a column type — never against the configuration source that produces it.
/// </summary>
[Collection("Database Lane 3")]
public class ProductVariationDefectTests : IntegrationTestBase
{
    private Guid _productId;
    private Guid _categoryId;
    private Guid _variationId;

    public ProductVariationDefectTests(DatabaseFixture databaseFixture)
        : base(databaseFixture)
    {
    }

    // ---- defect 1: the update path applied no variation rules at all ---------------------------

    /// <summary>
    /// THE regression test. <c>CreateProductVariationRules.Apply</c> was called from the create
    /// validator only, so this exact payload was a clean 400 on POST and reached the database on
    /// PUT. With the column now bounded it would be a 500 — a database error for what has always
    /// been a bad request.
    /// </summary>
    [Fact]
    public async Task AnOverlongVariationName_IsRefusedOnUpdate()
    {
        AuthenticateAsAdmin();

        var response = await PutRawAsync(BuildPayload(Variation(_variationId, new string('x', 300), 0)));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    /// <summary>The control: the same rule, the same message, on the path that always had it.</summary>
    [Fact]
    public async Task AnOverlongVariationName_IsStillRefusedOnCreate()
    {
        AuthenticateAsAdmin();

        var response = await PostRawAsync(BuildCreatePayload(new string('x', 300), 0));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task AnEmptyVariationName_IsRefusedOnUpdate()
    {
        AuthenticateAsAdmin();

        var response = await PutRawAsync(BuildPayload(Variation(_variationId, "", 0)));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ANegativeVariationDisplayOrder_IsRefusedOnUpdate()
    {
        AuthenticateAsAdmin();

        var response = await PutRawAsync(BuildPayload(Variation(_variationId, "Large", -1)));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    /// <summary>
    /// The other half of a validation fix, and the half that is easy to forget: an ordinary save
    /// still works. A rule applied to the wrong accessor would fail here and nowhere else.
    /// </summary>
    [Fact]
    public async Task AnOrdinaryVariationSave_StillSucceeds()
    {
        AuthenticateAsAdmin();

        var response = await PutRawAsync(BuildPayload(Variation(_variationId, "Extra large", 2)));

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var variation = await context.ProductVariations.SingleAsync(v => v.Id == _variationId);
        variation.Name.Should().Be("Extra large");
        variation.DisplayOrder.Should().Be(2);
    }

    // ---- defect 2: the missing unique index on the description twin -----------------------------

    /// <summary>
    /// Two <c>en</c> rows for one variation were storable, which is why <c>ProductDtoMapper</c> reads
    /// the language map through a <c>g.First()</c> and why which name won was whatever the database
    /// returned first. The ingredient twin has had this constraint since it was written.
    /// </summary>
    [Fact]
    public async Task ASecondDescriptionInTheSameLanguage_IsRefusedByTheDatabase()
    {
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        context.ProductVariationDescriptions.Add(new ProductVariationDescription
        {
            ProductVariationId = _variationId,
            LanguageCode = "en",
            Name = "Large (duplicate)",
            CreatedBy = "test",
        });

        var save = async () => await context.SaveChangesAsync();

        await save.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task ADescriptionInAnotherLanguage_IsStillAccepted()
    {
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        context.ProductVariationDescriptions.Add(new ProductVariationDescription
        {
            ProductVariationId = _variationId,
            LanguageCode = "it",
            Name = "Grande",
            CreatedBy = "test",
        });

        await context.SaveChangesAsync();

        var descriptions = await context.ProductVariationDescriptions
            .Where(d => d.ProductVariationId == _variationId)
            .ToListAsync();

        descriptions.Should().HaveCount(2);
    }

    // ---- defect 3: the entity had no configuration at all ---------------------------------------

    /// <summary>
    /// <c>ProductVariation</c> was mapped entirely by convention, which left two unbounded <c>text</c>
    /// columns and the only money column in the schema with no scale. Asserted through the EF model
    /// rather than the class, because the model is what produced the migration.
    /// </summary>
    [Fact]
    public void TheVariationColumns_AreBounded()
    {
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var entity = context.Model.FindEntityType(typeof(ProductVariation));

        entity!.FindProperty(nameof(ProductVariation.Name))!.GetMaxLength().Should().Be(200);
        entity.FindProperty(nameof(ProductVariation.Description))!.GetMaxLength().Should().Be(500);
        // `numeric`, not `decimal`: the configuration says decimal(18,2) and the Npgsql provider
        // resolves it to the store type the migration actually writes, which is what this asserts.
        entity.FindProperty(nameof(ProductVariation.PriceModifier))!.GetColumnType()
            .Should().Be("numeric(18,2)", "a money column without a scale is a rounding bug waiting to happen");
    }

    // ---- helpers -------------------------------------------------------------------------------

    private Task<HttpResponseMessage> PutRawAsync(string variationFragment) =>
        Client.PutAsync(
            $"/api/Products/{_productId}",
            new StringContent(variationFragment, Encoding.UTF8, "application/json"));

    private Task<HttpResponseMessage> PostRawAsync(string json) =>
        Client.PostAsync("/api/Products", new StringContent(json, Encoding.UTF8, "application/json"));

    private static string Variation(Guid id, string name, int displayOrder) => $$"""
    {
      "id": "{{id}}",
      "name": "{{name}}",
      "priceModifier": 2.0,
      "isActive": true,
      "displayOrder": {{displayOrder}}
    }
    """;

    private string BuildPayload(string variationFragment) => $$"""
    {
      "id": "{{_productId}}",
      "name": "S4 Defects Pizza",
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
      "variations": [{{variationFragment}}]
    }
    """;

    private string BuildCreatePayload(string name, int displayOrder) => $$"""
    {
      "name": "S4 Created Pizza",
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
      "content": { "en": { "name": "S4 Created Pizza", "description": "d" } },
      "variations": [
        {
          "name": "{{name}}",
          "priceModifier": 1.0,
          "isActive": true,
          "displayOrder": {{displayOrder}}
        }
      ]
    }
    """;

    protected override async Task SeedTestData()
    {
        await base.SeedTestData();

        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        _categoryId = (await context.Categories.OrderBy(c => c.Name).FirstAsync()).Id;

        var product = new Product
        {
            Name = "S4 Defects Pizza",
            BasePrice = 15m,
            IsActive = true,
            IsAvailable = true,
            Type = ProductType.MainItem,
            CreatedBy = "test",
        };
        product.ProductCategories.Add(new ProductCategory
        {
            CategoryId = _categoryId,
            IsPrimary = true,
            CreatedBy = "test",
        });

        var variation = new ProductVariation
        {
            Name = "Large",
            PriceModifier = 2m,
            IsActive = true,
            DisplayOrder = 0,
            CreatedBy = "test",
            Descriptions =
            {
                new ProductVariationDescription
                {
                    LanguageCode = "en",
                    Name = "Large",
                    CreatedBy = "test",
                },
            },
        };
        product.Variations.Add(variation);

        context.Add(product);
        await context.SaveChangesAsync();

        _productId = product.Id;
        _variationId = variation.Id;
    }
}
