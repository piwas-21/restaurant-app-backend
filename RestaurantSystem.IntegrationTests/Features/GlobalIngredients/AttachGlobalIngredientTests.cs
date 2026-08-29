using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.GlobalIngredients.Dtos;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.GlobalIngredients;

/// <summary>
/// Slice <b>S8</b> of SHARED-MODIFIERS-AND-SAUCES-PLAN — "reuse at scale": one library row copied
/// onto many products at once, which is the owner's original <i>"why must I retype this on 40
/// pizzas"</i>.
/// </summary>
/// <remarks>
/// <para>
/// <b>The rule this file exists for is the OPTIONAL one.</b> An order placed before the S1 snapshot
/// renders through <c>OrderIngredientCustomizations.ProjectRecipe</c> against the LIVE recipe, and
/// that method reports any REQUIRED ingredient missing from the line's saved id map as a removal.
/// So a bulk attach of a required ingredient prints "NO &lt;name&gt;" on every historic receipt and
/// kitchen ticket for every product it touched — plan D2 ("a past receipt never changes") broken
/// forty times by one click. <see cref="ARequiredIngredientWouldHaveRewrittenHistory"/> is the
/// CONTROL that proves this is a real mechanism rather than a story, by doing to the database
/// exactly what the endpoint refuses to do; without it,
/// <see cref="AnOptionalAttach_LeavesAHistoricOrderLineByteIdentical"/> could pass because the
/// fixture cannot express the danger.
/// </para>
/// <para>
/// The history assertions compare SERIALISED JSON, for the same reason S1's do: byte-identical is
/// the claim, and a field-wise comparison passes while the order of the lines changes.
/// </para>
/// </remarks>
[Collection("Database Lane 1")]
public class AttachGlobalIngredientTests : IntegrationTestBase
{
    private const string SauceLibraryName = "S8 Attach — Chilli oil";
    private const string HerbLibraryName = "S8 Attach — Oregano";
    private const string ArchivedLibraryName = "S8 Attach — Archived";
    private const string HistoricOrderNumber = "S8-ATTACH-HISTORIC";

    private static readonly Guid PizzaAId = Guid.NewGuid();
    private static readonly Guid PizzaBId = Guid.NewGuid();
    private static readonly Guid AlreadyLinkedProductId = Guid.NewGuid();
    private static readonly Guid ThinMarginProductId = Guid.NewGuid();
    private static readonly Guid PizzaADoughId = Guid.NewGuid();

    public AttachGlobalIngredientTests(DatabaseFixture databaseFixture)
        : base(databaseFixture)
    {
    }

    // ── the copy ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// TWO products, because a bulk write that happened to work for one proves nothing about the
    /// loop, and because "40 pizzas" is the whole request.
    /// </summary>
    [Fact]
    public async Task AttachingToTwoProducts_CopiesTheNameKindTranslationsAndProvenance()
    {
        var libraryId = await LibraryIdAsync(SauceLibraryName);

        var result = await AttachAsync(libraryId, [PizzaAId, PizzaBId]);

        result.AttachedProductIds.Should().BeEquivalentTo(new[] { PizzaAId, PizzaBId });
        result.Kind.Should().Be(
            IngredientKind.Sauce,
            "a body that states no kind falls back to the catalogue row's own — see TheGroupIsStatedByTheCaller_NotByTheCatalogueRow");

        foreach (var productId in new[] { PizzaAId, PizzaBId })
        {
            var attached = await AttachedRowAsync(productId, libraryId);

            attached.Name.Should().Be(SauceLibraryName);
            attached.Kind.Should().Be(IngredientKind.Sauce);
            attached.IsOptional.Should().BeTrue();
            attached.Price.Should().Be(1.50m);
            attached.MaxQuantity.Should().Be(2);
            attached.GlobalIngredientId.Should().Be(libraryId, "provenance, not propagation (plan D3)");

            var names = await TranslationsAsync(attached.Id);
            names.Should().BeEquivalentTo(new Dictionary<string, string>
            {
                ["fr"] = "Huile pimentée",
                ["tr"] = "Acı biber yağı",
            }, "the nine translations are what the admin would otherwise retype per product");
        }
    }

    /// <summary>
    /// WHICH GROUP the rows land in is stated by the CALLER, in both directions — the catalogue
    /// row's own kind is only the fallback.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the defect the slice exists for, and it was measured on a live tenant, not inferred:
    /// all 654 of its catalogue rows are typed <c>ingredient</c> because no admin write has ever
    /// sent a kind, so "apply Sauce blanche to 21 products" landed 21 rows in the INGREDIENTS group
    /// of 21 products. The picker had always stamped the GROUP it was opened from (plan D8); this
    /// endpoint stamped <c>library.Kind</c>. Two shipped paths, opposite rules, one decision.
    /// </para>
    /// <para>
    /// <b>BOTH directions, and that is what makes this a test rather than a demonstration.</b> A
    /// single "sauce wins" case would also pass against an implementation that merely promoted
    /// everything to <c>sauce</c>, or that read the catalogue row and happened to agree. The second
    /// row of the theory takes a catalogue row that IS a sauce and asks for it as an ingredient —
    /// harissa is a sauce on a kebab and an ingredient in a merguez — which no rule that reads the
    /// catalogue can satisfy. Each case is seeded so the catalogue row's kind is the OPPOSITE of
    /// what is asked for, and the last assertion pins that, so neither can pass vacuously.
    /// </para>
    /// <para>
    /// Together with <see cref="AttachingToTwoProducts_CopiesTheNameKindTranslationsAndProvenance"/>
    /// — same sauce row, no kind in the body, sauce rows out — this also pins that <c>null</c> and
    /// <c>"ingredient"</c> are NOT the same payload, which is the whole reason the field is nullable.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData(HerbLibraryName, IngredientKind.Sauce)]
    [InlineData(SauceLibraryName, IngredientKind.Ingredient)]
    public async Task TheGroupIsStatedByTheCaller_NotByTheCatalogueRow(string libraryName, IngredientKind stated)
    {
        var libraryId = await LibraryIdAsync(libraryName);

        var result = await AttachAsync(libraryId, [PizzaAId, PizzaBId], kind: stated);

        result.Kind.Should().Be(stated, "the receipt reports where the rows really went");
        foreach (var productId in new[] { PizzaAId, PizzaBId })
        {
            (await AttachedRowAsync(productId, libraryId)).Kind.Should().Be(stated);
        }

        (await LibraryKindAsync(libraryId)).Should().NotBe(
            stated,
            "the fixture must ASK FOR THE OPPOSITE of what the catalogue row says, or the assertion "
            + "above passes against the very rule this test exists to replace");
    }

    /// <summary>
    /// The new row goes ONE PAST THE HIGHEST POSITION IN USE, across both kinds — not at the row
    /// count, which is what this handler did first and what the front end's plain "Add variation"
    /// button did before frontend #593.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The oracles are computed by hand from the fixture, and the fixture is built so the two
    /// rules DISAGREE.</b> Pizza A holds a GAP — rows at 0 and 5 — so the count says 2 and the rule
    /// says 6; appending at 2 would have dropped the new row between the two existing ones while
    /// the admin was told it went to the end. Pizza B holds a DUPLICATE — two rows at 0 — so the
    /// count says 2 and the rule says 1; appending at 2 is harmless there, which is the point of
    /// having both: the count is not merely "sometimes high", it is unrelated to the answer.
    /// </para>
    /// <para>
    /// TWO products, because "40 pizzas" is the request and because a single product cannot show
    /// that the answer is derived per product rather than from the batch.
    /// </para>
    /// <para>
    /// The last two assertions are the CONTROL: they pin that the row count would have given a
    /// different number on both products. Without them this test passes against the defect on any
    /// contiguous fixture, which is how the defect got written.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task TheNewRow_IsAppendedPastTheHighestPositionInUse_NotAtTheRowCount()
    {
        var libraryId = await LibraryIdAsync(HerbLibraryName);

        await AttachAsync(libraryId, [PizzaAId, PizzaBId]);

        var pizzaA = await RecipeAsync(PizzaAId);
        var pizzaB = await RecipeAsync(PizzaBId);

        pizzaA.Single(r => r.GlobalIngredientId == libraryId).DisplayOrder
            .Should().Be(6, "Pizza A's recipe sits at 0 and 5, so the end is past 5");
        pizzaB.Single(r => r.GlobalIngredientId == libraryId).DisplayOrder
            .Should().Be(1, "Pizza B's two rows both sit at 0, so the end is past 0");

        pizzaA.Count.Should().Be(3);
        pizzaB.Count.Should().Be(3);
        pizzaA.Single(r => r.GlobalIngredientId == libraryId).DisplayOrder.Should().NotBe(pizzaA.Count - 1);
        pizzaB.Single(r => r.GlobalIngredientId == libraryId).DisplayOrder.Should().NotBe(pizzaB.Count - 1);
    }

    /// <summary>
    /// A bulk attach does not go AROUND the id-diffing writer — it never touches an existing row at
    /// all, which is strictly stronger than reconciling through it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The constraint this pins is the one S0 was written for: <c>OrderItem.IngredientQuantitiesJson</c>
    /// is a map keyed by <c>ProductIngredient.Id</c>, so re-keying a recipe blanks the ingredient
    /// detail of every past order of that product. A pure append cannot re-key anything.
    /// </para>
    /// <para>
    /// It also pins the TRANSLATIONS of the untouched rows, which is the failure mode of the
    /// tempting alternative: <c>ProductIngredientSynchronizer</c> replaces a supplied row's
    /// descriptions wholesale, so reconciling the whole recipe through it would delete the
    /// translations of every row the bulk projection did not carry. Ids alone would not catch that.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task TheOtherIngredientsKeepTheirIdsAndTheirTranslations()
    {
        var libraryId = await LibraryIdAsync(SauceLibraryName);
        var before = await RecipeFingerprintAsync(PizzaAId);

        await AttachAsync(libraryId, [PizzaAId]);

        var after = await RecipeFingerprintAsync(PizzaAId);
        after.Should().ContainKey(PizzaADoughId, "an existing row keeps its own id");
        foreach (var (id, fingerprint) in before)
        {
            after.Should().ContainKey(id);
            after[id].Should().Be(fingerprint, "a bulk attach appends and changes nothing else");
        }
    }

    // ── what it refuses, and what it merely skips ────────────────────────────────────────────

    /// <summary>
    /// Idempotent by PROVENANCE. Attaching twice must not give one product two copies of one library
    /// row — which would also make S3's "used on N items" a lie about N, since that count is
    /// DISTINCT by product.
    /// </summary>
    [Fact]
    public async Task AttachingToAProductThatAlreadyHasIt_ChangesNothingAndSaysWhy()
    {
        var libraryId = await LibraryIdAsync(SauceLibraryName);
        var before = (await RecipeAsync(AlreadyLinkedProductId)).Count;

        var result = await AttachAsync(libraryId, [AlreadyLinkedProductId]);

        result.AttachedProductIds.Should().BeEmpty();
        result.Skipped.Should().ContainSingle()
            .Which.Reason.Should().Be("alreadyLinked");
        (await RecipeAsync(AlreadyLinkedProductId)).Count.Should().Be(before);
    }

    /// <summary>
    /// Nothing is dropped in silence. A bulk action that reported success while quietly missing four
    /// of forty is the one that gets trusted wrongly.
    /// </summary>
    [Fact]
    public async Task AnUnknownProductId_IsReportedRatherThanIgnored()
    {
        var libraryId = await LibraryIdAsync(HerbLibraryName);
        var ghost = Guid.NewGuid();

        var result = await AttachAsync(libraryId, [PizzaBId, ghost]);

        result.AttachedProductIds.Should().Equal(PizzaBId);
        result.Skipped.Should().ContainSingle(s => s.ProductId == ghost && s.Reason == "notFound");
    }

    /// <summary>
    /// The same predicate <c>GlobalIngredientProvenance</c> applies to a new link on the product PUT.
    /// A bulk endpoint that accepted an archived row would be a second door back onto a shelf the
    /// admin deliberately cleared (plan D4).
    /// </summary>
    [Fact]
    public async Task AnArchivedLibraryRow_IsRefused()
    {
        AuthenticateAsAdmin();
        var archivedId = await ArchivedLibraryIdAsync();

        var response = await PostAsJsonAsync(
            $"/api/global-ingredients/{archivedId}/attach",
            new AttachGlobalIngredientDto { ProductIds = [PizzaAId], Price = 1m, IsIncludedInBasePrice = false });

        var body = await ReadResponseAsync<ApiResponse<AttachGlobalIngredientResultDto>>(response);
        body!.Success.Should().BeFalse();
        body.Errors.Should().ContainSingle().Which.Should().Contain("archived");
        (await RecipeAsync(PizzaAId)).Should().NotContain(r => r.GlobalIngredientId == archivedId);
    }

    /// <summary>
    /// The load-bearing refusal. See <see cref="ARequiredIngredientWouldHaveRewrittenHistory"/> for
    /// what it is protecting.
    /// </summary>
    [Fact]
    public async Task AttachingARequiredIngredient_IsRefused()
    {
        AuthenticateAsAdmin();
        var libraryId = await LibraryIdAsync(HerbLibraryName);

        var response = await PostAsJsonAsync(
            $"/api/global-ingredients/{libraryId}/attach",
            new AttachGlobalIngredientDto
            {
                ProductIds = [PizzaBId],
                IsOptional = false,
                Price = 1m,
                IsIncludedInBasePrice = false,
            });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await RecipeAsync(PizzaBId)).Should().NotContain(r => r.GlobalIngredientId == libraryId);
    }

    // ── history ──────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The regression the plan names in §8, applied to a bulk write: an existing order line is
    /// byte-identical afterwards.
    /// </summary>
    [Fact]
    public async Task AnOptionalAttach_LeavesAHistoricOrderLineByteIdentical()
    {
        var libraryId = await LibraryIdAsync(SauceLibraryName);
        var before = await RenderIngredientLinesAsync();

        await AttachAsync(libraryId, [PizzaAId]);

        var after = await RenderIngredientLinesAsync();
        after.Should().Be(before);
        before.Should().Contain("Dough", "a fixture that renders nothing would make this vacuous");
    }

    /// <summary>
    /// THE CONTROL, and the reason the endpoint refuses a required ingredient at all. It does to the
    /// database exactly what the endpoint will not do, and the historic line immediately gains a
    /// removal nobody made.
    /// </summary>
    [Fact]
    public async Task ARequiredIngredientWouldHaveRewrittenHistory()
    {
        var before = await RenderIngredientLinesAsync();

        await MutateCatalogAsync(context => context.ProductIngredients.Add(new ProductIngredient
        {
            ProductId = PizzaAId,
            Name = "S8 Control — Required",
            IsOptional = false,
            IsActive = true,
            MaxQuantity = 1,
            DisplayOrder = 99,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test",
        }));

        var after = await RenderIngredientLinesAsync();

        after.Should().NotBe(before);
        (await RenderIngredientRowsAsync()).Should().Contain(
            line => line.IngredientName == "S8 Control — Required" && line.IsRemoved,
            "ProjectRecipe reports a required ingredient absent from the saved id map as REMOVED, "
            + "so adding one in bulk would print a 'NO X' on every past ticket for the product");
    }

    // ── the money guard ──────────────────────────────────────────────────────────────────────

    /// <summary>
    /// All-or-nothing. Backend #432's guard exists because
    /// <c>BasketPricingService.CalculateIngredientCustomizationPrice</c> DEDUCTS the price of every
    /// optional, included-in-base ingredient the caller does not select — so a bulk write that
    /// pushed a product past its own ceiling would let anyone price a NEGATIVE line. The batch is
    /// refused whole, the offender is named, and the product that WOULD have been fine is not
    /// written either, because a partial bulk edit is the anti-pattern plan §6 buys protection from.
    /// </summary>
    [Fact]
    public async Task AnIncludedInBaseAttachThatWouldPriceBelowZero_RefusesTheWholeBatch()
    {
        AuthenticateAsAdmin();
        var libraryId = await LibraryIdAsync(HerbLibraryName);

        var response = await PostAsJsonAsync(
            $"/api/global-ingredients/{libraryId}/attach",
            new AttachGlobalIngredientDto
            {
                ProductIds = [ThinMarginProductId, PizzaBId],
                Price = 5.00m,
                IsIncludedInBasePrice = true,
            });

        var body = await ReadResponseAsync<ApiResponse<AttachGlobalIngredientResultDto>>(response);
        body!.Success.Should().BeFalse();
        // `Errors[0]`, not `Message`: the one-argument `ApiResponse.Failure` leaves `Message` at the
        // wrapper's own "Operation failed" and puts the reason in the list — which is exactly why
        // the frontend reads `errors[]`.
        body.Errors.Should().ContainSingle()
            .Which.Should().Contain("S8 Thin Margin", "the admin's fix is per product");

        (await RecipeAsync(ThinMarginProductId)).Should().NotContain(r => r.GlobalIngredientId == libraryId);
        (await RecipeAsync(PizzaBId)).Should().NotContain(r => r.GlobalIngredientId == libraryId,
            "nothing is written when one target of the batch would end up invalid");
    }

    /// <summary>
    /// The same body WITHOUT the included-in-base flag is fine on the very same thin-margin product:
    /// the guard is about the deduction, not about the price, and a control that could never pass
    /// would prove nothing about the one that fails.
    /// </summary>
    [Fact]
    public async Task ThePlainPricedAttach_IsAcceptedOnTheSameThinMarginProduct()
    {
        var libraryId = await LibraryIdAsync(SauceLibraryName);

        var result = await AttachAsync(libraryId, [ThinMarginProductId], price: 5.00m);

        result.AttachedProductIds.Should().Equal(ThinMarginProductId);
    }

    // ── the usage list ───────────────────────────────────────────────────────────────────────

    /// <summary>
    /// A confirm dialog that says "used on 3 items" above a list of four has one of them wrong. The
    /// list and S3's count must answer the same question.
    /// </summary>
    [Fact]
    public async Task TheUsageListAndTheCount_AgreeOnTheSameSet()
    {
        var libraryId = await LibraryIdAsync(SauceLibraryName);
        await AttachAsync(libraryId, [PizzaAId, PizzaBId]);

        var list = await GetFromJsonAsync<ApiResponse<List<CatalogUsageProductDto>>>(
            $"/api/global-ingredients/{libraryId}/products");
        var library = await GetFromJsonAsync<ApiResponse<List<GlobalIngredientDto>>>("/api/global-ingredients");
        var count = library!.Data!.Single(i => i.Id == libraryId).UsedOnProductCount;

        list!.Data!.Count.Should().Be(count);
        list.Data.Should().Contain(p => p.ProductId == AlreadyLinkedProductId,
            "the product seeded with the link is in both");
        list.Data.Select(p => p.ProductName).Should().BeInAscendingOrder(
            "the confirm dialog reads the same way twice");
    }

    // ── helpers ──────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// <paramref name="kind"/> defaults to <c>null</c>, which is the payload every caller written
    /// before backend #452 sends — "the field was omitted", NOT "put these in the Ingredients
    /// group". Keeping the default null is what lets the existing tests in this file go on pinning
    /// the fallback while the two new ones pin the stated case.
    /// </summary>
    private async Task<AttachGlobalIngredientResultDto> AttachAsync(
        Guid libraryId,
        List<Guid> productIds,
        decimal price = 1.50m,
        IngredientKind? kind = null)
    {
        AuthenticateAsAdmin();
        var response = await PostAsJsonAsync(
            $"/api/global-ingredients/{libraryId}/attach",
            new AttachGlobalIngredientDto
            {
                ProductIds = productIds,
                Kind = kind,
                Price = price,
                MaxQuantity = 2,
                IsIncludedInBasePrice = false,
            });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await ReadResponseAsync<ApiResponse<AttachGlobalIngredientResultDto>>(response);
        body!.Success.Should().BeTrue(body.Message);
        return body.Data!;
    }

    private async Task<Guid> LibraryIdAsync(string defaultName)
    {
        var library = await GetFromJsonAsync<ApiResponse<List<GlobalIngredientDto>>>("/api/global-ingredients");
        return library!.Data!.Single(i => i.DefaultName == defaultName).Id;
    }

    /// <summary>
    /// The catalogue row's OWN kind, read from the database — the control
    /// <see cref="TheGroupIsStatedByTheCaller_NotByTheCatalogueRow"/> needs to prove its fixture
    /// asks for the opposite of what the row says.
    /// </summary>
    private async Task<IngredientKind> LibraryKindAsync(Guid libraryId)
    {
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await context.GlobalIngredients
            .Where(g => g.Id == libraryId)
            .Select(g => g.Kind)
            .SingleAsync();
    }

    private async Task<Guid> ArchivedLibraryIdAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await context.GlobalIngredients
            .Where(g => g.DefaultName == ArchivedLibraryName)
            .Select(g => g.Id)
            .SingleAsync();
    }

    private async Task<List<ProductIngredient>> RecipeAsync(Guid productId)
    {
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await context.ProductIngredients
            .Where(i => i.ProductId == productId)
            .OrderBy(i => i.DisplayOrder)
            .ToListAsync();
    }

    /// <summary>
    /// Every row of a recipe as id → a serialised fingerprint of the fields a re-key or a wholesale
    /// description replace would move, TRANSLATIONS INCLUDED. Serialised for the same reason S1's
    /// assertions are: byte-identical is the claim, and a field-wise comparison passes while the set
    /// of translations quietly shrinks.
    /// </summary>
    private async Task<Dictionary<Guid, string>> RecipeFingerprintAsync(Guid productId)
    {
        var rows = await RecipeAsync(productId);
        var fingerprints = new Dictionary<Guid, string>();
        foreach (var row in rows)
        {
            var translations = await TranslationsAsync(row.Id);
            fingerprints[row.Id] = JsonSerializer.Serialize(new
            {
                row.Name,
                row.DisplayOrder,
                row.Price,
                row.IsOptional,
                row.IsIncludedInBasePrice,
                row.Kind,
                row.GlobalIngredientId,
                Translations = translations.OrderBy(t => t.Key).ToDictionary(t => t.Key, t => t.Value),
            });
        }

        return fingerprints;
    }

    private async Task<ProductIngredient> AttachedRowAsync(Guid productId, Guid libraryId) =>
        (await RecipeAsync(productId)).Single(i => i.GlobalIngredientId == libraryId);

    private async Task<Dictionary<string, string>> TranslationsAsync(Guid ingredientId)
    {
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await context.ProductIngredientDescriptions
            .Where(d => d.ProductIngredientId == ingredientId)
            .ToDictionaryAsync(d => d.LanguageCode, d => d.Name);
    }

    private async Task MutateCatalogAsync(Action<ApplicationDbContext> mutate)
    {
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        mutate(context);
        await context.SaveChangesAsync();
    }

    /// <summary>
    /// The ORACLE: what the order screen and the kitchen ticket actually render for the pre-S1 line,
    /// produced by the real mapper rather than re-derived here.
    /// </summary>
    private async Task<string> RenderIngredientLinesAsync() =>
        JsonSerializer.Serialize(await RenderIngredientRowsAsync());

    private async Task<List<OrderItemIngredientDto>?> RenderIngredientRowsAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var mapper = scope.ServiceProvider.GetRequiredService<IOrderMappingService>();

        var order = await context.Orders
            .Include(o => o.Items)
            .FirstAsync(o => o.OrderNumber == HistoricOrderNumber);
        var dto = await mapper.MapToOrderDtoAsync(order);

        return dto.Items.Single().IngredientCustomizations;
    }

    protected override async Task SeedTestData()
    {
        await base.SeedTestData();

        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var sauce = NewLibraryRow(SauceLibraryName, IngredientKind.Sauce);
        sauce.Translations.Add(new GlobalIngredientTranslation { LanguageCode = "fr", Name = "Huile pimentée", CreatedBy = "test" });
        sauce.Translations.Add(new GlobalIngredientTranslation { LanguageCode = "tr", Name = "Acı biber yağı", CreatedBy = "test" });

        var herb = NewLibraryRow(HerbLibraryName, IngredientKind.Ingredient);

        var archived = NewLibraryRow(ArchivedLibraryName, IngredientKind.Ingredient);
        archived.ArchivedAt = DateTime.UtcNow;
        archived.ArchivedBy = "test";

        // The two recipes are deliberately NOT contiguous, because live data is not: nothing wrote
        // DisplayOrder after row creation until frontend #593. Pizza A holds a GAP (0, 5) and Pizza B
        // holds a DUPLICATE (0, 0). Those are the two shapes that make "append at the row count"
        // wrong, and they make it wrong in OPPOSITE directions — see
        // TheNewRow_IsAppendedPastTheHighestPositionInUse_NotAtTheRowCount. A contiguous fixture
        // cannot fail against the count, which is exactly why this one is not contiguous.
        var pizzaA = NewProduct(PizzaAId, "S8 Pizza A", basePrice: 18m);
        var dough = NewIngredient(PizzaADoughId, "Dough", order: 0);
        // An existing row with translations of its OWN, so TheOtherIngredientsKeepTheirIdsAndTheirTranslations
        // has something to lose. A recipe of untranslated rows cannot express that failure.
        dough.Descriptions.Add(new ProductIngredientDescription { LanguageCode = "fr", Name = "Pâte", CreatedBy = "test" });
        dough.Descriptions.Add(new ProductIngredientDescription { LanguageCode = "tr", Name = "Hamur", CreatedBy = "test" });
        pizzaA.DetailedIngredients.Add(dough);
        pizzaA.DetailedIngredients.Add(NewIngredient(Guid.NewGuid(), "Tomato", order: 5));

        var pizzaB = NewProduct(PizzaBId, "S8 Pizza B", basePrice: 20m);
        pizzaB.DetailedIngredients.Add(NewIngredient(Guid.NewGuid(), "Dough", order: 0));
        pizzaB.DetailedIngredients.Add(NewIngredient(Guid.NewGuid(), "Cheese", order: 0));

        var alreadyLinked = NewProduct(AlreadyLinkedProductId, "S8 Pizza C", basePrice: 16m);
        var linkedRow = NewIngredient(Guid.NewGuid(), SauceLibraryName, order: 0);
        linkedRow.GlobalIngredient = sauce;
        alreadyLinked.DetailedIngredients.Add(linkedRow);

        // Base price 10, already carrying 8.00 of removable included-in-base value: one more at 5.00
        // would let an order that deselects everything price the line at -3.00.
        var thinMargin = NewProduct(ThinMarginProductId, "S8 Thin Margin", basePrice: 10m);
        var included = NewIngredient(Guid.NewGuid(), "Mezze", order: 0);
        // All THREE flags, because that is what MaxDeduction tests. A required row contributes
        // nothing to the deduction — the guest cannot deselect it — and seeding it required is how
        // this fixture first failed to express the state it exists to catch.
        included.IsOptional = true;
        included.IsIncludedInBasePrice = true;
        included.Price = 8.00m;
        thinMargin.DetailedIngredients.Add(included);

        context.AddRange(sauce, herb, archived);
        context.AddRange(pizzaA, pizzaB, alreadyLinked, thinMargin);
        await context.SaveChangesAsync();

        // A pre-S1 order line on Pizza A: an id map and NO snapshot rows, so it renders against the
        // live recipe — which is exactly the surface a bulk attach could damage.
        context.Orders.Add(new Order
        {
            Id = Guid.NewGuid(),
            OrderNumber = HistoricOrderNumber,
            Type = OrderType.DineIn,
            Status = OrderStatus.Confirmed,
            PaymentStatus = PaymentStatus.Pending,
            OrderDate = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test",
            Items =
            [
                new OrderItem
                {
                    Id = Guid.NewGuid(),
                    ProductId = PizzaAId,
                    ProductName = "S8 Pizza A",
                    Quantity = 1,
                    UnitPrice = 18m,
                    ItemTotal = 18m,
                    IngredientQuantitiesJson = JsonSerializer.Serialize(
                        new Dictionary<Guid, int> { [PizzaADoughId] = 1 }),
                    CreatedAt = DateTime.UtcNow,
                    CreatedBy = "test",
                },
            ],
        });
        await context.SaveChangesAsync();
    }

    private static GlobalIngredient NewLibraryRow(string defaultName, IngredientKind kind) => new()
    {
        DefaultName = defaultName,
        Kind = kind,
        IsActive = true,
        CreatedBy = "test",
    };

    private static Product NewProduct(Guid id, string name, decimal basePrice) => new()
    {
        Id = id,
        Name = name,
        BasePrice = basePrice,
        Type = ProductType.MainItem,
        IsActive = true,
        IsAvailable = true,
        Ingredients = [],
        Allergens = [],
        CreatedAt = DateTime.UtcNow,
        CreatedBy = "test",
    };

    private static ProductIngredient NewIngredient(Guid id, string name, int order) => new()
    {
        Id = id,
        Name = name,
        IsOptional = false,
        IsActive = true,
        MaxQuantity = 1,
        DisplayOrder = order,
        CreatedAt = DateTime.UtcNow,
        CreatedBy = "test",
    };
}
