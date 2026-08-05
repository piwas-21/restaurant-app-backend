using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Basket.Dtos;
using RestaurantSystem.Api.Features.Basket.Dtos.Requests;
using RestaurantSystem.Api.Features.Basket.Interfaces;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Common;
using RestaurantSystem.IntegrationTests.Infrastructure;
using System.Net;

namespace RestaurantSystem.IntegrationTests.Features.Basket;

// Issue #305: a bundle's child rows kept their ADD-TIME quantity forever.
//
// BasketItemFactory stores a child as `Quantity = item.Quantity * option.Quantity` — line-absolute —
// against a per-unit UnitPrice (the section's AdditionalPrice). At add time those two factors agree,
// so `child.Quantity * child.UnitPrice` is exactly the component's share of the line. Change the line
// quantity and they stop agreeing: the line really holds N times the components, and the row still
// says what it said when it was built.
//
// WHY NO EXISTING TEST CAUGHT IT, and why every test here reads CHILD ROWS: the money is right. The
// parent's UnitPrice carries the whole line price and is multiplied by the parent's own quantity, so
// basket totals reconcile before and after. Any assertion on totals — which is what the basket suites
// assert — passes with the defect fully present. Only the displayed count is wrong.
//
// Two call sites move a parent's quantity, and the issue named one. Both are covered here:
//   1. UpdateBasketItemAsync — the cart stepper (PUT /api/basket/items/{id}).
//   2. AnonymousBasketMerger — logging in while holding the same bundle in both baskets. Its match
//      keys on ProductId + variation and excludes only child rows, so a bundle merges exactly like a
//      standalone product.
//   3. AddItemToBasketAsync's exact-match merge (`exactMatch.Quantity +=`). An earlier version of
//      this comment claimed it "cannot reach a bundle, because the Menu branch returns before it".
//      That is true of bundle PARENTS and false of the thing that matters: the dedup query carried
//      no `ParentBasketItemId` filter, so it matched a bundle's CHILD rows — same BasketId, the
//      component's ProductId, a null variation id, and IsSameCustomization true for any
//      uncustomised add. Ordering a standalone Coke beside a bundle containing Coke therefore
//      merged into the bundle's child row: no line of the guest's own, the child's ItemTotal
//      stopped being 0, and the subtotal double-counted. The rescale then MULTIPLIED the polluted
//      count. Fixed here by filtering the query to root rows, which is what AnonymousBasketMerger
//      always did.
public class BundleChildQuantityRescaleTests : IntegrationTestBase
{
    private readonly string _sessionId = Guid.NewGuid().ToString();
    private Product _testPizza = null!;
    private Product _testCola = null!;
    private Product _menuProduct = null!;
    private MenuSection _mainSection = null!;
    private MenuSection _drinkSection = null!;
    private Guid _colaExtraShotId;

    private const decimal MenuBasePrice = 8.00m;
    private const decimal MainAdditional = 2.00m;
    private const decimal DrinkAdditional = 1.50m;
    private const decimal ExtraShotPrice = 1.50m;

    // What BuildMenuItemAsync stores on the parent of a bundle added by AddBundleViaServiceAsync:
    //   UnitPrice = 8.00 base + 2.00 main + (1.50 drink x 2) + (1.50 extra shot x 2) = 16.00
    //   CustomizationPrice = 1.50 x 2 = 3.00, ALREADY counted inside UnitPrice above
    // so the line total is UnitPrice * Quantity, and adding CustomizationPrice again charges the
    // extra shots twice (#308).
    private const decimal CustomisedBundleUnitPrice = 16.00m;
    private const decimal CustomisedBundleCustomizationPrice = ExtraShotPrice * DrinksPerBundle;

    // The drink option is taken TWICE per bundle, deliberately. With a per-unit count of 1 the
    // rescale is indistinguishable from "copy the parent's quantity onto the child", which is a
    // different (and wrong) rule that would pass every assertion. 2 separates them.
    private const int DrinksPerBundle = 2;

    public BundleChildQuantityRescaleTests(DatabaseFixture databaseFixture)
        : base(databaseFixture)
    {
    }

    protected override async Task SeedTestData()
    {
        await base.SeedTestData();

        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        _testPizza = await context.Products.FirstAsync(p => p.Name == "Test Pizza");
        _testCola = await context.Products.FirstAsync(p => p.Name == "Test Cola");

        // A priced optional extra on the drink, selected only by AddBundleViaServiceAsync. Without a
        // customization the bundle's CustomizationPrice is 0 and the two candidate ItemTotal formulas
        // AGREE, so the money assertion on the merge test below would pass against the unfixed code —
        // the "a test for a bug fix must fail without the fix" trap. Priced, optional and NOT included
        // in the base price is the one shape CalculateIngredientCustomizationPrice charges for.
        var extraShot = new ProductIngredient
        {
            Id = Guid.NewGuid(),
            ProductId = _testCola.Id,
            Name = "Extra Shot",
            IsOptional = true,
            IsIncludedInBasePrice = false,
            Price = ExtraShotPrice,
            MaxQuantity = 5,
            IsActive = true,
            DisplayOrder = 1,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test"
        };
        context.ProductIngredients.Add(extraShot);
        _colaExtraShotId = extraShot.Id;

        var menuProduct = new Product
        {
            Id = Guid.NewGuid(),
            Name = "Rescale Combo",
            BasePrice = MenuBasePrice,
            IsActive = true,
            IsAvailable = true,
            PreparationTimeMinutes = 15,
            Type = ProductType.Menu,
            Ingredients = new List<string>(),
            Allergens = new List<string>(),
            DisplayOrder = 30,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test"
        };

        var menuDefinition = new MenuDefinition
        {
            Id = Guid.NewGuid(),
            ProductId = menuProduct.Id,
            IsAlwaysAvailable = true,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test"
        };

        _mainSection = new MenuSection
        {
            Id = Guid.NewGuid(),
            MenuDefinitionId = menuDefinition.Id,
            Name = "Main",
            DisplayOrder = 1,
            IsRequired = true,
            MinSelection = 1,
            MaxSelection = 1,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test"
        };
        _drinkSection = new MenuSection
        {
            Id = Guid.NewGuid(),
            MenuDefinitionId = menuDefinition.Id,
            Name = "Drink",
            DisplayOrder = 2,
            IsRequired = true,
            MinSelection = 1,
            MaxSelection = DrinksPerBundle,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test"
        };

        _mainSection.Items.Add(new MenuSectionItem
        {
            Id = Guid.NewGuid(),
            MenuSectionId = _mainSection.Id,
            ProductId = _testPizza.Id,
            AdditionalPrice = MainAdditional,
            DisplayOrder = 1,
            IsDefault = true,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test"
        });
        _drinkSection.Items.Add(new MenuSectionItem
        {
            Id = Guid.NewGuid(),
            MenuSectionId = _drinkSection.Id,
            ProductId = _testCola.Id,
            AdditionalPrice = DrinkAdditional,
            DisplayOrder = 1,
            IsDefault = true,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test"
        });

        menuDefinition.Sections.Add(_mainSection);
        menuDefinition.Sections.Add(_drinkSection);
        menuProduct.MenuDefinition = menuDefinition;

        context.Products.Add(menuProduct);
        await context.SaveChangesAsync();

        _menuProduct = menuProduct;
    }

    private Task<HttpResponseMessage> AddBundleAsync(int quantity) =>
        PostAsJsonAsync("/api/basket/items", new AddToBasketDto
        {
            ProductId = _menuProduct.Id,
            Quantity = quantity,
            SelectedMenuOptions = new List<SelectedMenuOptionDto>
            {
                new() { SectionId = _mainSection.Id, ItemId = _testPizza.Id, Quantity = 1 },
                new() { SectionId = _drinkSection.Id, ItemId = _testCola.Id, Quantity = DrinksPerBundle }
            }
        });

    /// <summary>
    /// The same bundle as <see cref="AddBundleAsync"/> but with the drink's priced optional extra
    /// selected, so the parent carries a non-zero CustomizationPrice folded into its UnitPrice —
    /// the only shape on which the two candidate line-total formulas disagree (#308).
    /// </summary>
    private List<SelectedMenuOptionDto> CustomisedBundleOptions() => new()
    {
        new() { SectionId = _mainSection.Id, ItemId = _testPizza.Id, Quantity = 1 },
        new()
        {
            SectionId = _drinkSection.Id,
            ItemId = _testCola.Id,
            Quantity = DrinksPerBundle,
            SelectedIngredients = new List<Guid> { _colaExtraShotId }
        }
    };

    private Task<HttpResponseMessage> AddCustomisedBundleAsync(int quantity) =>
        PostAsJsonAsync("/api/basket/items", new AddToBasketDto
        {
            ProductId = _menuProduct.Id,
            Quantity = quantity,
            SelectedMenuOptions = CustomisedBundleOptions()
        });

    /// <summary>
    /// Child quantities straight from the DATABASE, keyed by product. Read from the rows rather than
    /// the DTO so the assertion cannot be satisfied by a mapping-layer multiplication — the fix has
    /// to land in stored state, which is what both surfaces and the order pipeline read.
    /// </summary>
    private async Task<Dictionary<Guid, int>> ReadChildQuantitiesAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var rows = await context.BasketItems
            .Where(bi => bi.Basket!.SessionId == _sessionId
                         && bi.ParentBasketItemId != null
                         && bi.ParentBasketItem!.ProductId == _menuProduct.Id)
            .Select(bi => new { bi.ProductId, bi.Quantity })
            .ToListAsync();

        return rows.ToDictionary(r => r.ProductId!.Value, r => r.Quantity);
    }

    private async Task<(Guid ParentId, decimal Total)> ReadParentAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var parent = await context.BasketItems
            .SingleAsync(bi => bi.ParentBasketItemId == null && bi.ProductId == _menuProduct.Id);

        return (parent.Id, parent.ItemTotal);
    }

    // ---- Site 1: the cart stepper -------------------------------------------------------------

    [Fact]
    public async Task RaisingTheLineQuantity_RescalesEveryChildRow()
    {
        Client.DefaultRequestHeaders.Add("X-Session-Id", _sessionId);

        (await AddBundleAsync(1)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await ReadChildQuantitiesAsync()).Should().BeEquivalentTo(new Dictionary<Guid, int>
        {
            [_testPizza.Id] = 1,
            [_testCola.Id] = DrinksPerBundle
        }, "the add-time counts are line-absolute at quantity 1");

        var (parentId, _) = await ReadParentAsync();

        var response = await PutAsJsonAsync($"/api/basket/items/{parentId}",
            new UpdateBasketItemDto { Quantity = 3 });
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        // 3 bundles: 3 pizzas, 6 colas. The cola row is what separates a real rescale from
        // "copy the parent quantity onto the child" — that rule would say 3 here.
        (await ReadChildQuantitiesAsync()).Should().BeEquivalentTo(new Dictionary<Guid, int>
        {
            [_testPizza.Id] = 3,
            [_testCola.Id] = DrinksPerBundle * 3
        });
    }

    [Fact]
    public async Task LoweringTheLineQuantity_RescalesBackDown()
    {
        Client.DefaultRequestHeaders.Add("X-Session-Id", _sessionId);

        (await AddBundleAsync(4)).StatusCode.Should().Be(HttpStatusCode.OK);
        var (parentId, _) = await ReadParentAsync();

        (await PutAsJsonAsync($"/api/basket/items/{parentId}",
            new UpdateBasketItemDto { Quantity = 2 })).StatusCode.Should().Be(HttpStatusCode.OK);

        // Down as well as up, because a fix written as "multiply by the new quantity" without
        // dividing by the old one is correct in exactly one direction from quantity 1.
        (await ReadChildQuantitiesAsync()).Should().BeEquivalentTo(new Dictionary<Guid, int>
        {
            [_testPizza.Id] = 2,
            [_testCola.Id] = DrinksPerBundle * 2
        });
    }

    [Fact]
    public async Task RepeatedQuantityChanges_DoNotCompound()
    {
        Client.DefaultRequestHeaders.Add("X-Session-Id", _sessionId);

        (await AddBundleAsync(1)).StatusCode.Should().Be(HttpStatusCode.OK);
        var (parentId, _) = await ReadParentAsync();

        // 1 → 5 → 2 → 4. A rescale that multiplies without dividing, or that reads the parent's
        // quantity AFTER the overwrite, drifts across these steps even when a single step looks
        // right.
        //
        // It ends at 4 rather than back at 1 ON PURPOSE. A round trip to the starting quantity
        // expects exactly the add-time counts, which is also what the rows hold when nothing
        // rescales them at all — so that version passed against the unfixed handler and proved
        // nothing. Measured, not assumed: it did pass, which is why this ends somewhere else.
        foreach (var quantity in new[] { 5, 2, 4 })
        {
            (await PutAsJsonAsync($"/api/basket/items/{parentId}",
                new UpdateBasketItemDto { Quantity = quantity })).StatusCode.Should().Be(HttpStatusCode.OK);
        }

        (await ReadChildQuantitiesAsync()).Should().BeEquivalentTo(new Dictionary<Guid, int>
        {
            [_testPizza.Id] = 4,
            [_testCola.Id] = DrinksPerBundle * 4
        }, "the counts must track the final quantity, not accumulate across the steps");

        // And now the round trip, which is the part that only detects DRIFT — it is meaningful
        // solely because the assertion above already established the rows are being rewritten.
        (await PutAsJsonAsync($"/api/basket/items/{parentId}",
            new UpdateBasketItemDto { Quantity = 1 })).StatusCode.Should().Be(HttpStatusCode.OK);

        (await ReadChildQuantitiesAsync()).Should().BeEquivalentTo(new Dictionary<Guid, int>
        {
            [_testPizza.Id] = 1,
            [_testCola.Id] = DrinksPerBundle
        }, "returning to the original quantity must return the original counts");
    }

    // The money leg. Not the subject of #305 — it was always right — but pinned because the fix
    // writes to rows that sit inside the totals calculation, and a child whose ItemTotal stopped
    // being 0 would double-count into every bundle basket.
    [Fact]
    public async Task RescalingChildren_LeavesTheChargeUnchanged()
    {
        Client.DefaultRequestHeaders.Add("X-Session-Id", _sessionId);

        (await AddBundleAsync(1)).StatusCode.Should().Be(HttpStatusCode.OK);
        var (parentId, singleTotal) = await ReadParentAsync();

        var response = await PutAsJsonAsync($"/api/basket/items/{parentId}",
            new UpdateBasketItemDto { Quantity = 3 });
        var basket = await ReadResponseAsync<ApiResponse<BasketDto>>(response);

        var (_, tripleTotal) = await ReadParentAsync();
        tripleTotal.Should().Be(singleTotal * 3);

        // The basket subtotal must equal the parent line alone — proof no child leaked into it.
        basket!.Data!.SubTotal.Should().Be(tripleTotal);

        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var childTotals = await context.BasketItems
            .Where(bi => bi.ParentBasketItemId != null)
            .Select(bi => bi.ItemTotal)
            .ToListAsync();
        childTotals.Should().OnlyContain(t => t == 0m,
            "children carry ItemTotal 0 by design so they cannot double-count against the parent");
    }

    // A quantity change that changes nothing must touch nothing. Pins the early return, which is
    // the difference between "no-op" and "rewrite every child row with the same number and a fresh
    // audit stamp".
    [Fact]
    public async Task SettingTheSameQuantity_LeavesChildRowsAndTheirAuditStampUntouched()
    {
        Client.DefaultRequestHeaders.Add("X-Session-Id", _sessionId);

        (await AddBundleAsync(2)).StatusCode.Should().Be(HttpStatusCode.OK);
        var (parentId, _) = await ReadParentAsync();

        var before = await ReadChildAuditAsync();

        (await PutAsJsonAsync($"/api/basket/items/{parentId}",
            new UpdateBasketItemDto { Quantity = 2 })).StatusCode.Should().Be(HttpStatusCode.OK);

        (await ReadChildAuditAsync()).Should().BeEquivalentTo(before);
    }

    private async Task<List<(Guid Product, int Qty, DateTime? UpdatedAt)>> ReadChildAuditAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var rows = await context.BasketItems
            .Where(bi => bi.Basket!.SessionId == _sessionId && bi.ParentBasketItemId != null)
            .Select(bi => new { bi.ProductId, bi.Quantity, bi.UpdatedAt })
            .ToListAsync();

        return rows
            .Select(r => (r.ProductId!.Value, r.Quantity, r.UpdatedAt))
            .OrderBy(r => r.Item1)
            .ToList();
    }

    // The SKIP branch: a child whose count is not an exact multiple of the old line quantity must be
    // LEFT ALONE, because its per-unit factor is not recoverable from the row.
    //
    // This test exists because the branch was measured to be completely unpinned — deleting the
    // `child.Quantity % previousQuantity != 0` guard passed all 119 basket tests. It carries the
    // longest comment in the scaler and is the one decision that changed during review, which makes
    // it exactly the code a later reader would "simplify".
    //
    // The corrupt state is seeded directly rather than through the API, deliberately: the two
    // in-app producers are now both closed (the add-path dedup and the child-id update), so the
    // only remaining source is a basket that was already live when this shipped. Seeding is
    // therefore the honest reproduction of the real case, not a shortcut around a guard.
    [Fact]
    public async Task AChildThatIsNotAMultipleOfTheLineQuantity_IsLeftAlone()
    {
        Client.DefaultRequestHeaders.Add("X-Session-Id", _sessionId);

        (await AddBundleAsync(4)).StatusCode.Should().Be(HttpStatusCode.OK);
        var (parentId, _) = await ReadParentAsync();

        // Force the cola child to 5 — not a multiple of the line quantity 4, so its per-unit count
        // cannot be divided back out.
        using (var scope = Factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var cola = await context.BasketItems.SingleAsync(bi =>
                bi.ParentBasketItemId == parentId && bi.ProductId == _testCola.Id);
            cola.Quantity = 5;
            await context.SaveChangesAsync();
        }

        var response = await PutAsJsonAsync($"/api/basket/items/{parentId}",
            new UpdateBasketItemDto { Quantity = 2 });
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var children = await ReadChildQuantitiesAsync();

        // Untouched — NOT 5*2/4 = 2 (a lossy guess) and NOT 1 (what the rejected Math.Max clamp
        // produced). The pizza child beside it is a clean multiple and rescales normally, which is
        // what proves the skip is per-row rather than an early exit from the whole loop.
        children[_testCola.Id].Should().Be(5, "a non-multiple child must be left exactly as found");
        children[_testPizza.Id].Should().Be(2, "its clean sibling must still rescale");

        // And the line's money still tracks the new quantity — the skip must not abort the update.
        var (_, total) = await ReadParentAsync();
        total.Should().BeGreaterThan(0m);
    }

    // The sibling half of the root-row invariant, in the method this PR edits. A bundle child is not
    // independently addressable, and before the filter `PUT` on one answered 200, set its ItemTotal
    // and double-counted the component into the subtotal (measured 13.00 -> 23.50).
    [Fact]
    public async Task UpdatingABundleChildDirectly_IsRefused()
    {
        Client.DefaultRequestHeaders.Add("X-Session-Id", _sessionId);

        (await AddBundleAsync(1)).StatusCode.Should().Be(HttpStatusCode.OK);
        var (parentId, _) = await ReadParentAsync();

        Guid childId;
        decimal subTotalBefore;
        using (var scope = Factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            childId = (await context.BasketItems.SingleAsync(bi =>
                bi.ParentBasketItemId == parentId && bi.ProductId == _testCola.Id)).Id;
            subTotalBefore = (await context.Baskets.SingleAsync(b => b.SessionId == _sessionId)).SubTotal;
        }

        var response = await PutAsJsonAsync($"/api/basket/items/{childId}",
            new UpdateBasketItemDto { Quantity = 7 });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);

        // The row and the money are both untouched — a 404 that still wrote would be worse than a 200.
        (await ReadChildQuantitiesAsync())[_testCola.Id].Should().Be(DrinksPerBundle);

        using var verify = Factory.Services.CreateScope();
        var db = verify.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        (await db.Baskets.SingleAsync(b => b.SessionId == _sessionId)).SubTotal.Should().Be(subTotalBefore);
        (await db.BasketItems.SingleAsync(bi => bi.Id == childId)).ItemTotal.Should().Be(0m);
    }

    // ---- Site 2: logging in with the same bundle in both baskets ------------------------------

    private async Task AddBundleViaServiceAsync(string? sessionId, Guid? userId, int quantity)
    {
        using var scope = Factory.Services.CreateScope();
        var basketService = scope.ServiceProvider.GetRequiredService<IBasketService>();
        await basketService.AddItemToBasketAsync(sessionId!, userId, new AddToBasketDto
        {
            ProductId = _menuProduct.Id,
            Quantity = quantity,
            // Carries the bundle's CustomizationPrice above 0, which is what makes the money
            // assertion in the merge test below able to fail. See ExtraShotPrice.
            SelectedMenuOptions = CustomisedBundleOptions()
        });
    }

    // The site #305 does not mention. AnonymousBasketMerger matches root rows on
    // ProductId + variation and excludes only CHILD rows, so a bundle held in both baskets merges
    // exactly like a standalone product — and the surviving parent's children keep the count they
    // were built with. Same defect, different door.
    [Fact]
    public async Task MergingAnonymousIntoUserBasket_RescalesTheSurvivingBundlesChildren()
    {
        var userId = Guid.Parse(TestAuthHandler.UserId);

        await AddBundleViaServiceAsync(null, userId, 2);       // already signed in, 2 bundles
        await AddBundleViaServiceAsync(_sessionId, null, 1);   // then browsing anonymously, 1 more

        using (var scope = Factory.Services.CreateScope())
        {
            var basketService = scope.ServiceProvider.GetRequiredService<IBasketService>();
            await basketService.MergeAnonymousBasketAsync(_sessionId, userId);
        }

        using var verifyScope = Factory.Services.CreateScope();
        var context = verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var userBasket = await context.Baskets
            .Include(b => b.Items)
            .SingleAsync(b => b.UserId == userId && !b.IsDeleted);

        var parent = userBasket.Items.Single(i =>
            i.ParentBasketItemId == null && i.ProductId == _menuProduct.Id);
        parent.Quantity.Should().Be(3, "2 held plus 1 merged in");

        var children = userBasket.Items
            .Where(i => i.ParentBasketItemId == parent.Id)
            .ToDictionary(i => i.ProductId!.Value, i => i.Quantity);

        children.Should().BeEquivalentTo(new Dictionary<Guid, int>
        {
            [_testPizza.Id] = 3,
            [_testCola.Id] = DrinksPerBundle * 3
        });

        // THE MONEY (#308). The merge used to recompute this as
        // `(UnitPrice + CustomizationPrice) * Quantity` — right for a regular item, and a double
        // charge for a bundle, because BuildMenuItemAsync already folds the customization into
        // UnitPrice. Measured at 57.00 against the 48.00 below, i.e. 9.00 overcharged on one line,
        // on a path that runs at every login where the same bundle sits in both baskets.
        //
        // The two operands are asserted alongside the total on purpose: they are what makes 48.00
        // the right answer rather than a number that happens to match, and CustomizationPrice
        // staying 3.00 is what says the fix changed the FORMULA rather than zeroing the input.
        parent.UnitPrice.Should().Be(CustomisedBundleUnitPrice);
        parent.CustomizationPrice.Should().Be(CustomisedBundleCustomizationPrice,
            "the customization must still be recorded for display — it is only counted once");
        parent.ItemTotal.Should().Be(CustomisedBundleUnitPrice * 3,
            "a bundle's UnitPrice already contains its customization, so the line total is UnitPrice * Quantity");
    }

    // The other end of the same asymmetry, on the stepper. `Quantity * UnitPrice` is already the
    // correct rule for a bundle, so this passed before #308 and must keep passing: it is the case a
    // fix that applied the regular-item formula everywhere would break, turning the merge's
    // overcharge into an identical overcharge one route over.
    [Fact]
    public async Task ChangingACustomisedBundlesQuantity_DoesNotDoubleChargeItsCustomization()
    {
        Client.DefaultRequestHeaders.Add("X-Session-Id", _sessionId);

        (await AddCustomisedBundleAsync(1)).StatusCode.Should().Be(HttpStatusCode.OK);

        Guid parentId;
        using (var scope = Factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var parent = await context.BasketItems.SingleAsync(bi =>
                bi.ParentBasketItemId == null && bi.ProductId == _menuProduct.Id);
            parent.UnitPrice.Should().Be(CustomisedBundleUnitPrice);
            parent.ItemTotal.Should().Be(CustomisedBundleUnitPrice, "add-time total at quantity 1");
            parentId = parent.Id;
        }

        var response = await PutAsJsonAsync($"/api/basket/items/{parentId}",
            new UpdateBasketItemDto { Quantity = 2 });
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var verifyScope = Factory.Services.CreateScope();
        var verifyContext = verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var updated = await verifyContext.BasketItems.SingleAsync(bi => bi.Id == parentId);

        updated.Quantity.Should().Be(2);
        updated.CustomizationPrice.Should().Be(CustomisedBundleCustomizationPrice);
        updated.ItemTotal.Should().Be(CustomisedBundleUnitPrice * 2, "32.00, not (16.00 + 3.00) * 2 = 38.00");
    }

    // ---- Site 3: the exact-match merge, which DOES reach a bundle's children -------------------

    // A standalone add of a product that is also a bundle component must create its own line and
    // leave the bundle's child row alone. Before the root-row filter it merged into that child:
    // measured as bundle child 2 -> 4 with ItemTotal 6.00 and NO standalone line, and a following
    // quantity change then multiplied the polluted count (4 -> 12).
    [Fact]
    public async Task StandaloneAddOfABundleComponent_DoesNotMergeIntoTheBundlesChildRow()
    {
        Client.DefaultRequestHeaders.Add("X-Session-Id", _sessionId);

        (await AddBundleAsync(1)).StatusCode.Should().Be(HttpStatusCode.OK);

        var response = await PostAsJsonAsync("/api/basket/items", new AddToBasketDto
        {
            ProductId = _testCola.Id,
            Quantity = 2
        });
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var items = await context.BasketItems
            .Where(bi => bi.Basket!.SessionId == _sessionId)
            .ToListAsync();

        // The guest's own cola line exists, at their own quantity.
        var standalone = items.Single(i => i.ParentBasketItemId == null && i.ProductId == _testCola.Id);
        standalone.Quantity.Should().Be(2);

        // And the bundle's cola child is untouched, still carrying ItemTotal 0.
        var parent = items.Single(i => i.ParentBasketItemId == null && i.ProductId == _menuProduct.Id);
        var child = items.Single(i => i.ParentBasketItemId == parent.Id && i.ProductId == _testCola.Id);
        child.Quantity.Should().Be(DrinksPerBundle, "the standalone add must not land on the child row");
        child.ItemTotal.Should().Be(0m, "children carry ItemTotal 0 so they cannot double-count");
    }

    // ---- The delete path, and what depends on it ----------------------------------------------

    // The fourth site in #308's header. DELETE looked a row up by id and basket with no
    // ParentBasketItemId filter, so a bundle CHILD was addressable: the component vanished from the
    // line while the parent's UnitPrice and CustomizationPrice still contained it, and the guest
    // went on paying for it.
    //
    // This also guards BasketLineTotal. Deleting children one at a time was the only way to reach a
    // bundle parent with CustomizationPrice > 0 and NO children — the single state the child-count
    // rule gets wrong. If this test ever goes red, that rule is no longer total.
    [Fact]
    public async Task DeletingABundleChildDirectly_IsRefused_AndLeavesTheLineIntact()
    {
        Client.DefaultRequestHeaders.Add("X-Session-Id", _sessionId);

        (await AddCustomisedBundleAsync(1)).StatusCode.Should().Be(HttpStatusCode.OK);

        Guid childId;
        using (var scope = Factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            childId = (await context.BasketItems.FirstAsync(bi =>
                bi.ParentBasketItemId != null && bi.ProductId == _testCola.Id)).Id;
        }

        var response = await Client.DeleteAsync($"/api/basket/items/{childId}");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);

        using var verifyScope = Factory.Services.CreateScope();
        var verifyContext = verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var items = await verifyContext.BasketItems
            .Where(bi => bi.Basket!.SessionId == _sessionId)
            .ToListAsync();

        items.Should().HaveCount(3, "the bundle parent and BOTH children survive the refused delete");
        var parent = items.Single(i => i.ParentBasketItemId == null);
        parent.ItemTotal.Should().Be(CustomisedBundleUnitPrice);
        parent.CustomizationPrice.Should().Be(CustomisedBundleCustomizationPrice);
    }

    // ---- The discriminator must NOT consult Product.Type ---------------------------------------

    // `Product.Type` is mutable — UpdateProductCommand assigns it with nothing checking for live
    // basket rows — so it cannot be used to decide how an EXISTING row was built. Retyped away from
    // Menu, this bundle parent stops returning early in AddItemToBasketAsync and falls into the
    // add-path dedup, which is where a type-based rule would price it as a regular item and add its
    // customization a second time: 38.00 instead of 32.00 on a 2 x 16.00 line.
    //
    // This is the test that fails if the `.Include(bi => bi.ChildBasketItems)` on the dedup query is
    // dropped, or if the type is ever reintroduced as an operand.
    [Fact]
    public async Task AddingAgainAfterTheBundlesProductIsRetyped_StillPricesItAsABundle()
    {
        Client.DefaultRequestHeaders.Add("X-Session-Id", _sessionId);

        (await AddCustomisedBundleAsync(1)).StatusCode.Should().Be(HttpStatusCode.OK);

        using (var scope = Factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var product = await context.Products.SingleAsync(p => p.Id == _menuProduct.Id);
            product.Type = ProductType.MainItem;
            await context.SaveChangesAsync();
        }

        var response = await PostAsJsonAsync("/api/basket/items", new AddToBasketDto
        {
            ProductId = _menuProduct.Id,
            Quantity = 1
        });
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var verifyScope = Factory.Services.CreateScope();
        var verifyContext = verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var parent = await verifyContext.BasketItems.SingleAsync(bi =>
            bi.ParentBasketItemId == null && bi.ProductId == _menuProduct.Id);

        parent.Quantity.Should().Be(2, "the retyped row dedups instead of returning early");
        parent.ItemTotal.Should().Be(CustomisedBundleUnitPrice * 2,
            "its UnitPrice still contains the customization regardless of what the product now says it is");
    }

    // ---- Bundle parents still never merge -----------------------------------------------------

    // Adding the same bundle twice must create a SECOND parent, not merge into the first — the
    // Menu branch returns before the exact-match code. This pins the half of that claim which IS
    // true; the half that was NOT (child rows are matched by the same query) is the header's site 3
    // and is fixed above. If bundle parents ever start merging, that path needs the rescale too.
    [Fact]
    public async Task AddingTheSameBundleTwice_CreatesASecondLine_RatherThanMerging()
    {
        Client.DefaultRequestHeaders.Add("X-Session-Id", _sessionId);

        (await AddBundleAsync(1)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await AddBundleAsync(1)).StatusCode.Should().Be(HttpStatusCode.OK);

        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var parents = await context.BasketItems
            .Where(bi => bi.ParentBasketItemId == null && bi.ProductId == _menuProduct.Id)
            .ToListAsync();

        parents.Should().HaveCount(2, "the Menu branch returns before the exact-match merge");
        parents.Should().OnlyContain(p => p.Quantity == 1);
    }
}
