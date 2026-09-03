using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Products.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Common;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.GlobalVariations;

/// <summary>
/// Plan D14, the second half: a variation or ingredient typed straight into the product editor was
/// saved on that product and nowhere else, so the same three sizes were retyped on every dish that
/// offered them while the library screen sat one click away and stayed empty of them.
///
/// <para>
/// These are end-to-end through the real product endpoints, not against the promotion classes,
/// because what was broken is the WIRING: the classes could be perfect and a command that never
/// called them would leave the defect exactly where it was.
/// </para>
/// </summary>
[Collection("Database Lane 3")]
public class CustomLibraryPromotionTests : IntegrationTestBase
{
    private const string ExistingName = "D14 Already On The Shelf";
    private const string ArchivedName = "D14 Taken Off The Shelf";

    private Guid _existingId;
    private Guid _categoryId;

    public CustomLibraryPromotionTests(DatabaseFixture databaseFixture)
        : base(databaseFixture)
    {
    }

    [Fact]
    public async Task AHandTypedVariation_LandsInTheTenantsOwnLibraryAndIsLinkedToIt()
    {
        AuthenticateAsAdmin();

        var created = await CreateProductAsync("D14 Promoted Variation", variationName: "D14 Enormous");

        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var row = await context.GlobalVariations.SingleAsync(g => g.DefaultName == "D14 Enormous");
        row.Origin.Should().Be(LibraryOrigin.Custom, "the tenant typed it, so the tenant may remove it");

        var variation = await context.ProductVariations.SingleAsync(v => v.ProductId == created);
        variation.GlobalVariationId.Should().Be(row.Id, "the product's copy records where the name came from");
    }

    /// <summary>
    /// Match first, create second. Without it, every product offering "Large" would mint its own
    /// row and the library would fill with the same word — which is the failure mode of promoting
    /// at all, and the reason this is one query rather than a blind insert.
    /// </summary>
    [Fact]
    public async Task AHandTypedNameAlreadyOnTheShelf_LinksToThatRowInsteadOfMintingASecond()
    {
        AuthenticateAsAdmin();

        // Deliberately a different CASE: the trimmed name is the only key either side has.
        var created = await CreateProductAsync("D14 Reuses The Shelf", variationName: ExistingName.ToUpperInvariant());

        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var rows = await context.GlobalVariations.CountAsync(g => g.DefaultName.ToLower() == ExistingName.ToLower());
        rows.Should().Be(1, "the shelf already held this word");

        var variation = await context.ProductVariations.SingleAsync(v => v.ProductId == created);
        variation.GlobalVariationId.Should().Be(_existingId);
    }

    /// <summary>
    /// Archiving is how an admin takes a name off the shelf. Re-linking would undo that by a side
    /// door and creating a twin would put the same word on two shelves, so the variation saves
    /// unpromoted — exactly what it did before promotion existed.
    /// </summary>
    [Fact]
    public async Task AHandTypedNameWhoseOnlyMatchIsArchived_IsNeitherLinkedNorDuplicated()
    {
        AuthenticateAsAdmin();

        var created = await CreateProductAsync("D14 Meets An Archived Name", variationName: ArchivedName);

        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var rows = await context.GlobalVariations.CountAsync(g => g.DefaultName == ArchivedName);
        rows.Should().Be(1, "the archived row is still the only one carrying that word");

        var variation = await context.ProductVariations.SingleAsync(v => v.ProductId == created);
        variation.GlobalVariationId.Should().BeNull("an archived row is off the shelf, so nothing new links to it");
    }

    /// <summary>
    /// The UPDATE path, which had no coverage at all: removing BOTH `?? variationPromotion.IdFor(…)`
    /// fallbacks from `UpdateProductCommand` left all 340 product + library tests green, because
    /// every case here went through POST. Two branches, and this exercises both — an EXISTING
    /// variation renamed by hand, and a NEW one appended by hand in the same PUT.
    /// </summary>
    [Fact]
    public async Task HandTypedVariationsOnAnUPDATE_ArePromotedOnBothBranches()
    {
        AuthenticateAsAdmin();
        var productId = await CreateProductAsync("D14 Updated Product", variationName: "D14 Original Size");

        Guid existingVariationId;
        using (var before = Factory.Services.CreateScope())
        {
            var context = before.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            existingVariationId = (await context.ProductVariations.SingleAsync(v => v.ProductId == productId)).Id;
        }

        var response = await PutAsJsonAsync($"/api/products/{productId}", new
        {
            // The command carries its own id and the handler refuses a mismatch.
            id = productId,
            name = "D14 Updated Product",
            basePrice = 10m,
            isActive = true,
            isAvailable = true,
            isSpecial = false,
            preparationTimeMinutes = 5,
            type = "MainItem",
            kitchenType = "BackKitchen",
            displayOrder = 0,
            categoryIds = new[] { _categoryId },
            primaryCategoryId = _categoryId,
            content = new Dictionary<string, object> { ["en"] = new { name = "D14 Updated Product", description = "x" } },
            variations = new object[]
            {
                // branch 1: an existing row, renamed by hand
                new { id = existingVariationId, name = "D14 Renamed Size", priceModifier = 1m, isActive = true, displayOrder = 0 },
                // branch 2: a brand-new row, typed by hand
                new { name = "D14 Appended Size", priceModifier = 2m, isActive = true, displayOrder = 1 },
            },
        });
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        foreach (var name in new[] { "D14 Renamed Size", "D14 Appended Size" })
        {
            var row = await db.GlobalVariations.SingleAsync(g => g.DefaultName == name);
            row.Origin.Should().Be(LibraryOrigin.Custom);
            var variation = await db.ProductVariations.SingleAsync(v => v.ProductId == productId && v.Name == name);
            variation.GlobalVariationId.Should().Be(row.Id, $"the {name} branch must consult the promotion too");
        }
    }

    /// <summary>
    /// The translations are the whole value of a library row — a pick copies nine names the admin
    /// would otherwise retype — so promoting a bare `DefaultName` would fill the shelf with
    /// untranslated words and leave the retyping exactly where it was.
    /// </summary>
    [Fact]
    public async Task APromotedRow_CarriesTheTranslationsTheAdminAlreadyTyped()
    {
        AuthenticateAsAdmin();

        await CreateProductAsync(
            "D14 Translated Variation",
            variationName: "D14 Family Size",
            variationContent: new Dictionary<string, object>
            {
                ["fr"] = new { name = "D14 Format Famille" },
                ["de"] = new { name = "D14 Familiengröße" },
            });

        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var row = await context.GlobalVariations
            .Include(g => g.Translations)
            .SingleAsync(g => g.DefaultName == "D14 Family Size");

        row.Translations.Should().HaveCount(2);
        row.Translations.Should().Contain(t => t.LanguageCode == "fr" && t.Name == "D14 Format Famille");
        row.Translations.Should().Contain(t => t.LanguageCode == "de" && t.Name == "D14 Familiengröße");
        // No blank-translation case here: the product validator refuses one ("A translation's name
        // is required"), so an admin cannot send it. The `IsNullOrWhiteSpace` filter in
        // `CustomVariationPromotion` stays as defence in depth against a future caller that can.
    }

    /// <summary>The sauce half: a hand-typed sauce must not be filed under Ingredients.</summary>
    [Fact]
    public async Task AHandTypedSauce_LandsInTheLibraryAsASauce()
    {
        AuthenticateAsAdmin();

        await CreateProductAsync("D14 Promoted Sauce", ingredientName: "D14 Smoked Garlic", kind: IngredientKind.Sauce);

        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var row = await context.GlobalIngredients.SingleAsync(g => g.DefaultName == "D14 Smoked Garlic");
        row.Kind.Should().Be(IngredientKind.Sauce, "the picker offers each catalog to its own group");
        row.Origin.Should().Be(LibraryOrigin.Custom);
    }

    // ---- helpers -------------------------------------------------------------------------------

    private async Task<Guid> CreateProductAsync(
        string productName,
        string? variationName = null,
        string? ingredientName = null,
        IngredientKind kind = IngredientKind.Ingredient,
        Dictionary<string, object>? variationContent = null)
    {
        var payload = new
        {
            name = productName,
            description = productName,
            basePrice = 10m,
            isActive = true,
            isAvailable = true,
            isSpecial = false,
            preparationTimeMinutes = 5,
            type = "MainItem",
            kitchenType = "BackKitchen",
            ingredients = Array.Empty<string>(),
            allergens = Array.Empty<string>(),
            displayOrder = 0,
            categoryIds = new[] { _categoryId },
            primaryCategoryId = _categoryId,
            content = new Dictionary<string, object>
            {
                ["en"] = new { name = productName, description = productName },
            },
            variations = variationName is null
                ? Array.Empty<object>()
                : [new { name = variationName, priceModifier = 1m, isActive = true, displayOrder = 0, content = variationContent }],
            detailedIngredients = ingredientName is null
                ? Array.Empty<object>()
                : [new
                {
                    name = ingredientName,
                    isOptional = true,
                    price = 1m,
                    isIncludedInBasePrice = false,
                    isActive = true,
                    displayOrder = 0,
                    maxQuantity = 1,
                    kind = kind.ToString(),
                }],
        };

        var response = await PostAsJsonAsync("/api/products", payload);
        var body = await ReadResponseAsync<ApiResponse<ProductDto>>(response);
        body!.Success.Should().BeTrue(body.Message);
        return body.Data!.Id;
    }

    protected override async Task SeedTestData()
    {
        await base.SeedTestData();

        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var category = new Category { Name = "D14 Category", IsActive = true, CreatedBy = "test" };
        var existing = new GlobalVariation
        {
            DefaultName = ExistingName,
            IsActive = true,
            Origin = LibraryOrigin.Custom,
            CreatedBy = "test",
        };
        var archived = new GlobalVariation
        {
            DefaultName = ArchivedName,
            IsActive = true,
            Origin = LibraryOrigin.Custom,
            ArchivedAt = DateTime.UtcNow,
            ArchivedBy = "test",
            CreatedBy = "test",
        };

        context.AddRange(category, existing, archived);
        await context.SaveChangesAsync();

        _existingId = existing.Id;
        _categoryId = category.Id;
    }
}
