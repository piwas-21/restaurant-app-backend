using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.GlobalVariations.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.GlobalVariations;

/// <summary>
/// Slice <b>S8</b> of SHARED-MODIFIERS-AND-SAUCES-PLAN — the VARIATION half of "reuse at scale".
/// </summary>
/// <remarks>
/// <para>
/// <b>The rule this file exists for is the PRICE FLOOR one, and it is not the ingredient rule with
/// the words changed.</b> <c>IncludedInBaseDeductionRule</c> compares the most a line can be deducted
/// by against the cheapest price the line can be SOLD at, and a variation's price modifier may be
/// negative — so a variation moves the second number while touching neither the first nor any
/// ingredient. Attaching one discount to forty products is therefore a money defect the ingredient
/// endpoint cannot produce, and <see cref="ADiscountThatWouldPriceTheLineBelowZero_RefusesTheWholeBatch"/>
/// is what proves the guard runs on this path too.
/// </para>
/// <para>
/// <b>What is deliberately NOT here: a "required" rule.</b> The ingredient attach refuses a required
/// row because a pre-S1 order line renders against the LIVE recipe. A variation has no such reader —
/// <c>OrderItem.VariationName</c> has been frozen at checkout since long before this plan — and
/// <see cref="AHistoricOrdersVariationName_IsUnchangedByABulkAttach"/> is the control that shows the
/// difference is real rather than an omission.
/// </para>
/// </remarks>
[Collection("Database Lane 3")]
public class AttachGlobalVariationTests : IntegrationTestBase
{
    private const string SizeLibraryName = "S8 Var — Large";
    private const string SecondLibraryName = "S8 Var — Small";
    private const string ArchivedLibraryName = "S8 Var — Archived";
    private const string HistoricOrderNumber = "S8-VAR-HISTORIC";

    private static readonly Guid PizzaAId = Guid.NewGuid();
    private static readonly Guid PizzaBId = Guid.NewGuid();
    private static readonly Guid AlreadyLinkedProductId = Guid.NewGuid();
    private static readonly Guid ThinMarginProductId = Guid.NewGuid();

    public AttachGlobalVariationTests(DatabaseFixture databaseFixture)
        : base(databaseFixture)
    {
    }

    // ── the copy ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// TWO products, because a bulk write that happened to work for one proves nothing about the
    /// loop, and because "40 pizzas" is the whole request.
    /// </summary>
    [Fact]
    public async Task AttachingToTwoProducts_CopiesTheNameTranslationsAndProvenance()
    {
        var libraryId = await LibraryIdAsync(SizeLibraryName);

        var result = await AttachAsync(libraryId, [PizzaAId, PizzaBId], priceModifier: 2.00m);

        result.AttachedProductIds.Should().BeEquivalentTo(new[] { PizzaAId, PizzaBId });

        foreach (var productId in new[] { PizzaAId, PizzaBId })
        {
            var attached = await AttachedRowAsync(productId, libraryId);

            attached.Name.Should().Be(SizeLibraryName);
            attached.PriceModifier.Should().Be(2.00m, "the price is the one fact the library cannot know");
            attached.IsActive.Should().BeTrue();
            attached.GlobalVariationId.Should().Be(libraryId, "provenance, not propagation (plan D3)");

            var names = await TranslationsAsync(attached.Id);
            names.Should().BeEquivalentTo(new Dictionary<string, string>
            {
                ["fr"] = "Grande",
                ["tr"] = "Büyük",
            }, "the translations are what the admin would otherwise retype per product");
        }
    }

    /// <summary>
    /// The new row goes ONE PAST THE HIGHEST POSITION IN USE — not at the row count.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The oracles are computed by hand from the fixture, and the fixture is built so the two
    /// rules DISAGREE.</b> Pizza A holds a GAP — rows at 0 and 4 — so the count says 2 and the rule
    /// says 5. Pizza B holds a DUPLICATE — two rows at 3 — so the count says 2 and the rule says 4.
    /// The count is not merely "sometimes low"; it is unrelated to the answer, and appending at it
    /// would drop a row the admin was told went to the END into the middle of a hand-arranged list,
    /// or on top of an existing position with the tie broken by whatever the database felt like.
    /// </para>
    /// <para>
    /// <c>useVariationReorder</c> is the reason the column looks like this: nothing wrote
    /// <c>DisplayOrder</c> after row creation until frontend #593, so live data holds gaps and
    /// duplicates. The last two assertions are the CONTROL that the count would have answered
    /// differently on BOTH products — without them the test passes against the defect on any
    /// contiguous fixture.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task TheNewVariation_IsAppendedPastTheHighestPositionInUse_NotAtTheRowCount()
    {
        var libraryId = await LibraryIdAsync(SecondLibraryName);

        await AttachAsync(libraryId, [PizzaAId, PizzaBId], priceModifier: 0m);

        var pizzaA = await VariationsAsync(PizzaAId);
        var pizzaB = await VariationsAsync(PizzaBId);

        pizzaA.Single(v => v.GlobalVariationId == libraryId).DisplayOrder
            .Should().Be(5, "Pizza A's variations sit at 0 and 4, so the end is past 4");
        pizzaB.Single(v => v.GlobalVariationId == libraryId).DisplayOrder
            .Should().Be(4, "Pizza B's two variations both sit at 3, so the end is past 3");

        pizzaA.Count.Should().Be(3);
        pizzaB.Count.Should().Be(3);
        pizzaA.Single(v => v.GlobalVariationId == libraryId).DisplayOrder.Should().NotBe(pizzaA.Count - 1);
        pizzaB.Single(v => v.GlobalVariationId == libraryId).DisplayOrder.Should().NotBe(pizzaB.Count - 1);
    }

    // ── the money guard, which is this endpoint's own ────────────────────────────────────────

    /// <summary>
    /// A NEGATIVE modifier moves the price FLOOR, and the whole batch is refused before anything is
    /// written.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The oracle is arithmetic on the fixture, not a re-run of the rule: "S8 Var Thin Margin" sells
    /// at 10.00 and carries one optional, included-in-base, active ingredient priced 8.00. With no
    /// variation the cheapest sellable unit is 10.00 and 8.00 fits under it. A −3.00 modifier makes
    /// the cheapest unit 7.00, so an order that deselects the 8.00 ingredient prices the line at
    /// −1.00 — reachable by anyone through <c>POST /api/orders</c> since backend #430.
    /// </para>
    /// <para>
    /// The batch also names a HEALTHY product, and that is the load-bearing half: it asserts the
    /// refusal is all-or-nothing across the batch rather than a per-product skip, which is what
    /// plan §6's "no irreversible bulk edit" buys. The message names the offender, because the
    /// admin's fix is per product and "invalid request" would not say which of forty to open.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task ADiscountThatWouldPriceTheLineBelowZero_RefusesTheWholeBatch()
    {
        var libraryId = await LibraryIdAsync(SizeLibraryName);
        var healthyBefore = (await VariationsAsync(PizzaAId)).Count;

        AuthenticateAsAdmin();
        var response = await PostAsJsonAsync(
            $"/api/global-variations/{libraryId}/attach",
            new AttachGlobalVariationDto
            {
                ProductIds = [PizzaAId, ThinMarginProductId],
                PriceModifier = -3.00m,
            });

        var body = await ReadResponseAsync<ApiResponse<AttachGlobalVariationResultDto>>(response);
        body!.Success.Should().BeFalse();
        // `Errors[0]`, not `Message`: the one-argument `ApiResponse.Failure` leaves `Message` at the
        // wrapper's own "Operation failed" and puts the reason in the list — which is why the
        // frontend reads `errors[]`.
        body.Errors.Should().ContainSingle()
            .Which.Should().Contain("S8 Var Thin Margin", "the admin's fix is per product");

        (await VariationsAsync(ThinMarginProductId)).Should().NotContain(v => v.GlobalVariationId == libraryId);
        (await VariationsAsync(PizzaAId)).Count.Should().Be(
            healthyBefore, "nothing is written when ONE target of the batch would end up invalid");
    }

    /// <summary>
    /// The control for the refusal above: the SAME product and the SAME endpoint accept a modifier
    /// that keeps the floor above the deduction. Without it, the refusal test passes against a
    /// handler that refuses everything.
    /// </summary>
    /// <remarks>
    /// −1.00 puts the cheapest sellable unit at 9.00 against an 8.00 deduction, so it fits — and it
    /// is still a DISCOUNT, which is what makes this a control on the rule rather than on the sign.
    /// </remarks>
    [Fact]
    public async Task ASmallerDiscount_IsAcceptedOnTheSameThinMarginProduct()
    {
        var libraryId = await LibraryIdAsync(SecondLibraryName);

        var result = await AttachAsync(libraryId, [ThinMarginProductId], priceModifier: -1.00m);

        result.AttachedProductIds.Should().ContainSingle().Which.Should().Be(ThinMarginProductId);
    }

    // ── what it merely skips ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Idempotent by PROVENANCE. Attaching twice must not give one product two copies of one library
    /// row — which would also make "used on N items" a lie about N, since that count is DISTINCT by
    /// product.
    /// </summary>
    [Fact]
    public async Task AttachingToAProductThatAlreadyHasIt_ChangesNothingAndSaysWhy()
    {
        var libraryId = await LibraryIdAsync(SizeLibraryName);
        var before = (await VariationsAsync(AlreadyLinkedProductId)).Count;

        var result = await AttachAsync(libraryId, [AlreadyLinkedProductId], priceModifier: 1m);

        result.AttachedProductIds.Should().BeEmpty();
        result.Skipped.Should().ContainSingle().Which.Reason.Should().Be("alreadyLinked");
        (await VariationsAsync(AlreadyLinkedProductId)).Count.Should().Be(before);
    }

    /// <summary>
    /// Skipping is by PROVENANCE, not by NAME — a hand-typed "S8 Var — Large" is not evidence that
    /// the library row is already there, and treating it as such would silently do nothing on a
    /// product the admin explicitly selected.
    /// </summary>
    /// <remarks>
    /// This is the sharp edge of the idempotence rule, so it is pinned rather than left implied:
    /// the same product carries a same-named variation with NO link, and the attach still lands.
    /// The duplicate-name question belongs to the confirm screen, which can SHOW the admin what a
    /// name check alone would have guessed at.
    /// </remarks>
    [Fact]
    public async Task ASameNamedVariationWithNoLink_DoesNotCountAsAlreadyAttached()
    {
        var libraryId = await LibraryIdAsync(SizeLibraryName);

        var result = await AttachAsync(libraryId, [PizzaBId], priceModifier: 1m);

        result.AttachedProductIds.Should().ContainSingle().Which.Should().Be(PizzaBId);
        var rows = await VariationsAsync(PizzaBId);
        rows.Where(v => v.Name == SizeLibraryName).Should().HaveCount(
            2, "the hand-typed one stays and the library-linked one is added beside it");
        rows.Count(v => v.GlobalVariationId == libraryId).Should().Be(1);
    }

    /// <summary>
    /// Nothing is dropped in silence. A bulk action that reported success while quietly missing four
    /// of forty is the one that gets trusted wrongly.
    /// </summary>
    [Fact]
    public async Task AnUnknownProductId_IsReportedRatherThanIgnored()
    {
        var libraryId = await LibraryIdAsync(SecondLibraryName);
        var ghost = Guid.NewGuid();

        var result = await AttachAsync(libraryId, [PizzaAId, ghost], priceModifier: 1m);

        result.AttachedProductIds.Should().ContainSingle().Which.Should().Be(PizzaAId);
        var skipped = result.Skipped.Should().ContainSingle().Subject;
        skipped.ProductId.Should().Be(ghost);
        skipped.Reason.Should().Be("notFound");
    }

    /// <summary>
    /// An archived library row is off the shelf, and a bulk endpoint must not be a second door back
    /// onto it (plan D4) — the same predicate <c>GlobalVariationProvenance</c> applies to a new link
    /// on the product PUT.
    /// </summary>
    [Fact]
    public async Task AnArchivedLibraryRow_IsRefused()
    {
        var archivedId = await ArchivedLibraryIdAsync();

        AuthenticateAsAdmin();
        var response = await PostAsJsonAsync(
            $"/api/global-variations/{archivedId}/attach",
            new AttachGlobalVariationDto { ProductIds = [PizzaAId], PriceModifier = 1m });

        var body = await ReadResponseAsync<ApiResponse<AttachGlobalVariationResultDto>>(response);
        body!.Success.Should().BeFalse();
        (await VariationsAsync(PizzaAId)).Should().NotContain(v => v.GlobalVariationId == archivedId);
    }

    // ── history, and the blast-radius list ───────────────────────────────────────────────────

    /// <summary>
    /// A past receipt never changes. The variation name an order line shows was frozen at checkout,
    /// so a bulk attach cannot reword it — which is WHY this endpoint needs no "required" rule of
    /// the kind the ingredient one has.
    /// </summary>
    /// <remarks>
    /// The control is the second assertion: the order line names a variation that no longer matches
    /// any live row, so if the read path had fallen back to the catalogue the attach would have
    /// changed it, and this test would be asserting a tautology instead of a property.
    /// </remarks>
    [Fact]
    public async Task AHistoricOrdersVariationName_IsUnchangedByABulkAttach()
    {
        var libraryId = await LibraryIdAsync(SizeLibraryName);
        var before = await HistoricVariationNameAsync();

        await AttachAsync(libraryId, [PizzaAId], priceModifier: 2m);

        (await HistoricVariationNameAsync()).Should().Be(before);
        before.Should().Be("Family size, as sold in 2026");
    }

    /// <summary>
    /// The list and the count answer the same question, because the confirm dialog renders both: a
    /// screen that says "used on 3 items" above a list of 4 has one of them wrong.
    /// </summary>
    /// <remarks>
    /// The count comes from the library list endpoint (S4's <c>usedOnProductCount</c>) and the list
    /// from the new one, so this compares two independently written queries rather than one query
    /// with itself. Quantity is 2 — one product before the attach and two after — because a single
    /// product cannot show that the two agree while COUNTING, only while existing.
    /// </remarks>
    [Fact]
    public async Task TheUsageListAndTheCount_AgreeOnTheSameSet()
    {
        var libraryId = await LibraryIdAsync(SizeLibraryName);

        await AttachAsync(libraryId, [PizzaAId, PizzaBId], priceModifier: 2m);

        AuthenticateAsAdmin();
        var list = await GetFromJsonAsync<ApiResponse<List<CatalogUsageProductDto>>>(
            $"/api/global-variations/{libraryId}/products");
        var count = (await LibraryRowAsync(SizeLibraryName)).UsedOnProductCount;

        list!.Data!.Should().HaveCount(count);
        list.Data!.Select(p => p.ProductId).Should().Contain([PizzaAId, PizzaBId]);
        count.Should().BeGreaterThan(1, "one product could not tell a count from a constant");
    }

    // ── helpers ──────────────────────────────────────────────────────────────────────────────

    private async Task<AttachGlobalVariationResultDto> AttachAsync(
        Guid libraryId,
        List<Guid> productIds,
        decimal priceModifier)
    {
        AuthenticateAsAdmin();
        var response = await PostAsJsonAsync(
            $"/api/global-variations/{libraryId}/attach",
            new AttachGlobalVariationDto { ProductIds = productIds, PriceModifier = priceModifier });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await ReadResponseAsync<ApiResponse<AttachGlobalVariationResultDto>>(response);
        body!.Success.Should().BeTrue(body.Message);
        return body.Data!;
    }

    private async Task<GlobalVariationDto> LibraryRowAsync(string defaultName)
    {
        var library = await GetFromJsonAsync<ApiResponse<List<GlobalVariationDto>>>("/api/global-variations");
        return library!.Data!.Single(v => v.DefaultName == defaultName);
    }

    private async Task<Guid> LibraryIdAsync(string defaultName) => (await LibraryRowAsync(defaultName)).Id;

    private async Task<Guid> ArchivedLibraryIdAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await context.GlobalVariations
            .Where(v => v.DefaultName == ArchivedLibraryName)
            .Select(v => v.Id)
            .SingleAsync();
    }

    private async Task<List<ProductVariation>> VariationsAsync(Guid productId)
    {
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await context.ProductVariations
            .Where(v => v.ProductId == productId)
            .OrderBy(v => v.DisplayOrder)
            .ToListAsync();
    }

    private async Task<ProductVariation> AttachedRowAsync(Guid productId, Guid libraryId) =>
        (await VariationsAsync(productId)).Single(v => v.GlobalVariationId == libraryId);

    private async Task<Dictionary<string, string>> TranslationsAsync(Guid variationId)
    {
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await context.ProductVariationDescriptions
            .Where(d => d.ProductVariationId == variationId)
            .ToDictionaryAsync(d => d.LanguageCode, d => d.Name);
    }

    private async Task<string?> HistoricVariationNameAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await context.Orders
            .Where(o => o.OrderNumber == HistoricOrderNumber)
            .SelectMany(o => o.Items)
            .Select(i => i.VariationName)
            .SingleAsync();
    }

    protected override async Task SeedTestData()
    {
        await base.SeedTestData();

        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var large = NewLibraryRow(SizeLibraryName);
        large.Translations.Add(new GlobalVariationTranslation { LanguageCode = "fr", Name = "Grande", CreatedBy = "test" });
        large.Translations.Add(new GlobalVariationTranslation { LanguageCode = "tr", Name = "Büyük", CreatedBy = "test" });

        var small = NewLibraryRow(SecondLibraryName);

        var archived = NewLibraryRow(ArchivedLibraryName);
        archived.ArchivedAt = DateTime.UtcNow;
        archived.ArchivedBy = "test";

        // The two variation lists are deliberately NOT contiguous, because live data is not: Pizza A
        // holds a GAP (0, 4) and Pizza B a DUPLICATE (3, 3). Those are the two shapes that make
        // "append at the row count" wrong — see
        // TheNewVariation_IsAppendedPastTheHighestPositionInUse_NotAtTheRowCount.
        var pizzaA = NewProduct(PizzaAId, "S8 Var Pizza A", basePrice: 18m);
        pizzaA.Variations.Add(NewVariation("Regular", order: 0, modifier: 0m));
        pizzaA.Variations.Add(NewVariation("XL", order: 4, modifier: 4m));

        var pizzaB = NewProduct(PizzaBId, "S8 Var Pizza B", basePrice: 20m);
        pizzaB.Variations.Add(NewVariation("Regular", order: 3, modifier: 0m));
        // Hand-typed and same-named, with NO provenance link — the fixture for
        // ASameNamedVariationWithNoLink_DoesNotCountAsAlreadyAttached.
        pizzaB.Variations.Add(NewVariation(SizeLibraryName, order: 3, modifier: 3m));

        var alreadyLinked = NewProduct(AlreadyLinkedProductId, "S8 Var Pizza C", basePrice: 16m);
        var linkedRow = NewVariation(SizeLibraryName, order: 0, modifier: 2m);
        linkedRow.GlobalVariation = large;
        alreadyLinked.Variations.Add(linkedRow);

        // Sells at 10.00 and carries 8.00 of removable included-in-base value, so a −3.00 modifier
        // puts the cheapest sellable unit at 7.00 and an order that deselects everything at −1.00.
        var thinMargin = NewProduct(ThinMarginProductId, "S8 Var Thin Margin", basePrice: 10m);
        var included = new ProductIngredient
        {
            Name = "Mezze",
            // All THREE flags, because that is what MaxDeduction tests — a required row contributes
            // nothing to the deduction, since the guest cannot deselect it.
            IsOptional = true,
            IsIncludedInBasePrice = true,
            IsActive = true,
            Price = 8.00m,
            MaxQuantity = 1,
            DisplayOrder = 0,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test",
        };
        thinMargin.DetailedIngredients.Add(included);

        context.AddRange(large, small, archived);
        context.AddRange(pizzaA, pizzaB, alreadyLinked, thinMargin);
        await context.SaveChangesAsync();

        // An order whose frozen VariationName matches NO live variation row — so a read path that
        // fell back to the catalogue would visibly change it.
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
                    ProductName = "S8 Var Pizza A",
                    VariationName = "Family size, as sold in 2026",
                    Quantity = 1,
                    UnitPrice = 18m,
                    ItemTotal = 18m,
                    IngredientQuantitiesJson = JsonSerializer.Serialize(new Dictionary<Guid, int>()),
                    CreatedAt = DateTime.UtcNow,
                    CreatedBy = "test",
                },
            ],
        });
        await context.SaveChangesAsync();
    }

    private static GlobalVariation NewLibraryRow(string defaultName) => new()
    {
        DefaultName = defaultName,
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

    private static ProductVariation NewVariation(string name, int order, decimal modifier) => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        DisplayOrder = order,
        PriceModifier = modifier,
        IsActive = true,
        CreatedAt = DateTime.UtcNow,
        CreatedBy = "test",
    };
}
