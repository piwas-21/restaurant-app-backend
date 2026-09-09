using System.Net;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.GlobalIngredients.Dtos;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.GlobalIngredients;

/// <summary>
/// Propagation for the word a product copy borrowed: one set of per-locale names written onto the
/// library row AND onto every copy of it, answering the partner complaint that fixing one product
/// left the same wrong word on every other product that copied the row.
/// </summary>
/// <remarks>
/// <para>
/// The fixture keeps TWO library rows so the two match rules cannot contaminate each other: the
/// CHILLI row owns the linked copies (A, B and a soft-deleted product), the OREGANO row exists only
/// as a default name for the LEGACY copy (C) that predates provenance — a null
/// <c>GlobalIngredientId</c> and nothing but the name to go on. A payload aimed at one row never
/// matches the other's copies, so every count below is exact.
/// </para>
/// <para>
/// Product C also carries a null-id copy named nothing like any row (the "Basil" control): the
/// name-match rule must reach a same-named legacy copy and no further.
/// </para>
/// </remarks>
[Collection("Database Lane 2")]
public class ApplyGlobalIngredientTranslationsTests : IntegrationTestBase
{
    private const string ChilliLibraryName = "S9 Apply — Chilli oil";
    private const string OreganoLibraryName = "S9 Apply — Oregano";

    private const string ChilliFrOld = "Huile pimentée";
    private const string ChilliTrOld = "Acı biber yağı";
    private const string PreservedFrDescription = "Piment doux — à préserver";

    private static readonly Guid ProductAId = Guid.NewGuid();
    private static readonly Guid ProductBId = Guid.NewGuid();
    private static readonly Guid LegacyProductId = Guid.NewGuid();
    private static readonly Guid DeletedProductId = Guid.NewGuid();
    private static readonly Guid ChilliCopyAId = Guid.NewGuid();
    private static readonly Guid ChilliCopyBId = Guid.NewGuid();
    private static readonly Guid ChilliCopyDeletedId = Guid.NewGuid();
    private static readonly Guid OreganoCopyId = Guid.NewGuid();
    private static readonly Guid BasilCopyId = Guid.NewGuid();

    public ApplyGlobalIngredientTranslationsTests(DatabaseFixture databaseFixture)
        : base(databaseFixture)
    {
    }

    // ── the propagation ───────────────────────────────────────────────────────────────────────

    /// <summary>
    /// BOTH linked copies, on both products, and the library row itself. The tr entry rides along
    /// only to prove the payload does not have to be complete: fr is the fix, tr updates in place.
    /// </summary>
    [Fact]
    public async Task ApplyingTranslations_UpdatesLinkedCopiesOnEveryProduct_AndTheLibraryRow()
    {
        var libraryId = await ChilliLibraryIdAsync();

        var result = await ApplyAsync(libraryId,
            new GlobalIngredientTranslationDto { LanguageCode = "fr", Name = "Huile pimentée forte" },
            new GlobalIngredientTranslationDto { LanguageCode = "tr", Name = "Acı biber yağı (düzeltme)" });

        result.UpdatedProductCount.Should().Be(2);
        result.UpdatedIngredientCount.Should().Be(2);
        result.Items.Should().BeEquivalentTo(new[]
        {
            new { ProductId = ProductAId, ProductName = "S9 Pizza A", IngredientId = ChilliCopyAId },
            new { ProductId = ProductBId, ProductName = "S9 Pizza B", IngredientId = ChilliCopyBId },
        });

        var libraryNames = await LibraryTranslationNamesAsync(libraryId);
        libraryNames.Should().ContainKey("fr").WhoseValue.Should().Be("Huile pimentée forte");
        libraryNames.Should().ContainKey("tr").WhoseValue.Should().Be("Acı biber yağı (düzeltme)");

        (await DescriptionNamesAsync(ChilliCopyAId))["fr"].Should().Be("Huile pimentée forte");
        (await DescriptionNamesAsync(ChilliCopyBId))["tr"].Should().Be("Acı biber yağı (düzeltme)");
    }

    /// <summary>
    /// A row typed by hand before provenance existed has no <c>GlobalIngredientId</c> — the name it
    /// shares with the library row IS the reference. The "Basil" control pins that the same rule
    /// reaches nothing beyond that name.
    /// </summary>
    [Fact]
    public async Task ALegacyCopyWithoutProvenance_IsReachedByName_AndOnlyByName()
    {
        var oreganoId = await LibraryIdAsync(OreganoLibraryName);

        var result = await ApplyAsync(oreganoId,
            new GlobalIngredientTranslationDto { LanguageCode = "fr", Name = "Origan frais" });

        result.UpdatedProductCount.Should().Be(1);
        result.Items.Should().ContainSingle().Which.IngredientId.Should().Be(OreganoCopyId);

        (await DescriptionNamesAsync(OreganoCopyId))["fr"].Should().Be("Origan frais");
        (await DescriptionNamesAsync(BasilCopyId))["fr"].Should().Be("Basilic");
        (await LibraryTranslationNamesAsync(oreganoId)).Should().ContainKey("fr")
            .WhoseValue.Should().Be("Origan frais");
    }

    /// <summary>
    /// THE partner-visible rule: the fix retitles the copy, it does not touch the description the
    /// product typed for itself. And the languages the payload is silent about keep their words —
    /// omitted means unchanged, on the row and on the copy.
    /// </summary>
    [Fact]
    public async Task UpdatingAnExistingLanguage_PreservesTheDescription_AndOtherLanguages()
    {
        var libraryId = await ChilliLibraryIdAsync();

        await ApplyAsync(libraryId,
            new GlobalIngredientTranslationDto { LanguageCode = "fr", Name = "Huile pimentée forte" });

        var descriptions = await DescriptionsAsync(ChilliCopyAId);
        descriptions["fr"].Name.Should().Be("Huile pimentée forte");
        descriptions["fr"].Description.Should().Be(PreservedFrDescription);
        descriptions["tr"].Name.Should().Be(ChilliTrOld,
            "a language the payload does not carry is never nulled out");

        var libraryNames = await LibraryTranslationNamesAsync(libraryId);
        libraryNames.Should().ContainKey("tr").WhoseValue.Should().Be(ChilliTrOld);
    }

    /// <summary>
    /// A language the copy never had is INSERTED, with a null description — the endpoint owns the
    /// name and nothing else, and it must not invent content for a product it only retitled.
    /// </summary>
    [Fact]
    public async Task AMissingLanguage_IsInserted_WithANullDescription()
    {
        var libraryId = await ChilliLibraryIdAsync();

        await ApplyAsync(libraryId,
            new GlobalIngredientTranslationDto { LanguageCode = "de", Name = "Chilliöl" });

        var descriptions = await DescriptionsAsync(ChilliCopyBId);
        descriptions.Should().ContainKey("de").WhoseValue.Name.Should().Be("Chilliöl");
        descriptions["de"].Description.Should().BeNull();

        (await LibraryTranslationNamesAsync(libraryId)).Should().ContainKey("de")
            .WhoseValue.Should().Be("Chilliöl");
    }

    // ── the guards ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AnUnknownLibraryRow_IsNotFound()
    {
        AuthenticateAsAdmin();
        var response = await PostAsJsonAsync(
            $"/api/global-ingredients/{Guid.NewGuid()}/apply-translations",
            new List<GlobalIngredientTranslationDto>
            { new() { LanguageCode = "fr", Name = "N'importe quoi" } });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var body = await ReadResponseAsync<ApiResponse<ApplyGlobalIngredientTranslationsResultDto>>(response);
        body!.Success.Should().BeFalse();
    }

    /// <summary>
    /// Deleted product, real copy: the propagation reaches through <c>Products</c>, so the global
    /// soft-delete filter decides — a product the catalogue no longer admits cannot be edited by a
    /// bulk fix, and the receipt must not claim otherwise.
    /// </summary>
    [Fact]
    public async Task ASoftDeletedProductsCopy_IsNeverTouched()
    {
        var libraryId = await ChilliLibraryIdAsync();

        var result = await ApplyAsync(libraryId,
            new GlobalIngredientTranslationDto { LanguageCode = "fr", Name = "Huile pimentée forte" });

        result.Items.Should().NotContain(item => item.ProductId == DeletedProductId);

        var descriptions = await DeletedProductDescriptionsAsync(ChilliCopyDeletedId);
        descriptions.Should().ContainKey("fr").WhoseValue.Name.Should().Be(ChilliFrOld);
    }

    /// <summary>
    /// Archived is "off the shelf, still linked" (plan D4): its copies keep rendering its words, so
    /// a translation fix must reach them. Refusing here would strand exactly the rows that are no
    /// longer editable through the picker.
    /// </summary>
    [Fact]
    public async Task AnArchivedRow_IsStillAValidSubject()
    {
        var libraryId = await ChilliLibraryIdAsync();
        await MutateCatalogAsync(context =>
        {
            var row = context.GlobalIngredients.Single(g => g.Id == libraryId);
            row.ArchivedAt = DateTime.UtcNow;
            row.ArchivedBy = "test";
        });

        var result = await ApplyAsync(libraryId,
            new GlobalIngredientTranslationDto { LanguageCode = "fr", Name = "Huile pimentée forte" });

        result.UpdatedProductCount.Should().Be(2);
        (await LibraryTranslationNamesAsync(libraryId))["fr"].Should().Be("Huile pimentée forte");
    }

    // ── the fixture ───────────────────────────────────────────────────────────────────────────

    protected override async Task SeedTestData()
    {
        await base.SeedTestData();

        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var chilli = NewLibraryRow(ChilliLibraryName);
        chilli.Translations.Add(new GlobalIngredientTranslation { LanguageCode = "fr", Name = ChilliFrOld, CreatedBy = "test" });
        chilli.Translations.Add(new GlobalIngredientTranslation { LanguageCode = "tr", Name = ChilliTrOld, CreatedBy = "test" });

        // Product A: the copy with BOTH languages already present, the fr one carrying the product's
        // own description — the row UpdatingAnExistingLanguage protects.
        var pizzaA = NewProduct(ProductAId, "S9 Pizza A");
        var copyA = NewCopy(ChilliCopyAId, ChilliLibraryName, chilli);
        copyA.Descriptions.Add(new ProductIngredientDescription { LanguageCode = "fr", Name = ChilliFrOld, Description = PreservedFrDescription, CreatedBy = "test" });
        copyA.Descriptions.Add(new ProductIngredientDescription { LanguageCode = "tr", Name = ChilliTrOld, CreatedBy = "test" });
        pizzaA.DetailedIngredients.Add(copyA);

        // Product B: a copy with NO translations at all — every payload language is an insert.
        var pizzaB = NewProduct(ProductBId, "S9 Pizza B");
        pizzaB.DetailedIngredients.Add(NewCopy(ChilliCopyBId, ChilliLibraryName, chilli));

        // A DELETED product still holding a live copy — the negative control for the soft-delete
        // filter, read back below with IgnoreQueryFilters because no filtered query can see it.
        var deleted = NewProduct(DeletedProductId, "S9 Deleted Pizza");
        deleted.IsDeleted = true;
        var copyDeleted = NewCopy(ChilliCopyDeletedId, ChilliLibraryName, chilli);
        copyDeleted.Descriptions.Add(new ProductIngredientDescription { LanguageCode = "fr", Name = ChilliFrOld, CreatedBy = "test" });
        deleted.DetailedIngredients.Add(copyDeleted);

        // The LEGACY shape: two hand-typed rows, no provenance. The oregano one shares the library
        // row's default name and is the target; the basil one shares nothing and must not move.
        var oregano = NewLibraryRow(OreganoLibraryName);
        var legacyProduct = NewProduct(LegacyProductId, "S9 Pizza C");
        var oreganoCopy = NewCopy(OreganoCopyId, OreganoLibraryName, library: null);
        oreganoCopy.Descriptions.Add(new ProductIngredientDescription { LanguageCode = "fr", Name = "Origan", Description = "Feuilles séchées", CreatedBy = "test" });
        legacyProduct.DetailedIngredients.Add(oreganoCopy);
        var basilCopy = NewCopy(BasilCopyId, "Basil", library: null);
        basilCopy.Descriptions.Add(new ProductIngredientDescription { LanguageCode = "fr", Name = "Basilic", CreatedBy = "test" });
        legacyProduct.DetailedIngredients.Add(basilCopy);

        context.AddRange(chilli, oregano);
        context.AddRange(pizzaA, pizzaB, deleted, legacyProduct);
        await context.SaveChangesAsync();
    }

    private static GlobalIngredient NewLibraryRow(string defaultName) => new()
    {
        DefaultName = defaultName,
        IsActive = true,
        CreatedBy = "test",
    };

    private static Product NewProduct(Guid id, string name) => new()
    {
        Id = id,
        Name = name,
        BasePrice = 18m,
        Type = Domain.Common.Enums.ProductType.MainItem,
        IsActive = true,
        IsAvailable = true,
        Ingredients = [],
        Allergens = [],
        CreatedAt = DateTime.UtcNow,
        CreatedBy = "test",
    };

    /// <summary>
    /// With <paramref name="library"/> set the copy is LINKED (provenance written); with null it is
    /// the LEGACY shape — a bare name and a null <c>GlobalIngredientId</c>.
    /// </summary>
    private static ProductIngredient NewCopy(Guid id, string name, GlobalIngredient? library) => new()
    {
        Id = id,
        Name = name,
        GlobalIngredient = library,
        IsOptional = true,
        IsActive = true,
        MaxQuantity = 1,
        CreatedAt = DateTime.UtcNow,
        CreatedBy = "test",
    };

    // ── the readers ───────────────────────────────────────────────────────────────────────────

    private async Task<ApplyGlobalIngredientTranslationsResultDto> ApplyAsync(
        Guid libraryId,
        params GlobalIngredientTranslationDto[] translations)
    {
        AuthenticateAsAdmin();
        var response = await PostAsJsonAsync(
            $"/api/global-ingredients/{libraryId}/apply-translations",
            translations.ToList());

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await ReadResponseAsync<ApiResponse<ApplyGlobalIngredientTranslationsResultDto>>(response);
        body!.Success.Should().BeTrue(body.Message);
        return body.Data!;
    }

    private async Task<Guid> ChilliLibraryIdAsync() => await LibraryIdAsync(ChilliLibraryName);

    private async Task MutateCatalogAsync(Action<ApplicationDbContext> mutate)
    {
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        mutate(context);
        await context.SaveChangesAsync();
    }

    private async Task<Guid> LibraryIdAsync(string defaultName)
    {
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await context.GlobalIngredients
            .Where(g => g.DefaultName == defaultName)
            .Select(g => g.Id)
            .SingleAsync();
    }

    private async Task<Dictionary<string, string>> LibraryTranslationNamesAsync(Guid libraryId)
    {
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await context.GlobalIngredientTranslations
            .Where(t => t.GlobalIngredientId == libraryId)
            .ToDictionaryAsync(t => t.LanguageCode, t => t.Name);
    }

    private async Task<Dictionary<string, ProductIngredientDescription>> DescriptionsAsync(Guid copyId)
    {
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await context.ProductIngredientDescriptions
            .Where(d => d.ProductIngredientId == copyId)
            .ToDictionaryAsync(d => d.LanguageCode);
    }

    private async Task<Dictionary<string, string>> DescriptionNamesAsync(Guid copyId) =>
        (await DescriptionsAsync(copyId)).ToDictionary(pair => pair.Key, pair => pair.Value.Name);

    /// <summary>
    /// The only reader that can see the deleted product's copy: the soft-delete filter hides it
    /// from every query above, which is the entire claim of ASoftDeletedProductsCopy_IsNeverTouched.
    /// </summary>
    private async Task<Dictionary<string, ProductIngredientDescription>> DeletedProductDescriptionsAsync(Guid copyId)
    {
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await context.ProductIngredientDescriptions
            .IgnoreQueryFilters()
            .Where(d => d.ProductIngredientId == copyId)
            .ToDictionaryAsync(d => d.LanguageCode);
    }
}
