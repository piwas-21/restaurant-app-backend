using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Infrastructure;
using System.Net;
using System.Net.Http.Json;
using System.Text;

namespace RestaurantSystem.IntegrationTests.Features.Menus;

// Issue #191: the guard around the section wipe could almost never be false.
// MenuDefinitionDto.Sections was `List<MenuSectionDto> Sections { get; init; } = new()`, so STJ
// left an ABSENT key as `[]` — `Sections != null` was true for the omitted case exactly as it was
// for the explicit-empty one, the RemoveRange ran, and the loop re-added nothing. Only a literal
// JSON `null`, which no client sends, took the preserving branch.
//
// Fixed at the contract, not the branch: the property lost its initializer and became nullable, and
// both write paths now REQUIRE the key. Under a full-replace PUT a third "leave sections alone"
// state would only trade silent data loss for a silent no-op, so the decision was to make omission
// LOUD. `[]` keeps its one honest meaning — the user deleted every section, which MenuSectionEditor
// genuinely lets them do — and that capability is pinned here alongside the rejection.
//
// The product path carried the identical dead guard (UpdateProductCommand.cs), so fixing only the
// bundle handler would have left `PUT /api/Products` on a Menu-type product able to wipe the same
// rows through the same shared DTO. Both are covered below.
public class MenuDefinitionSectionsRequiredTests : IntegrationTestBase
{
    private Guid _bundleId;
    private Guid _categoryId;
    private Guid _componentProductId;

    public MenuDefinitionSectionsRequiredTests(DatabaseFixture databaseFixture)
        : base(databaseFixture)
    {
    }

    protected override async Task SeedTestData()
    {
        await base.SeedTestData();

        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        _categoryId = (await context.Categories.OrderBy(c => c.Name).FirstAsync()).Id;
        _componentProductId = (await context.Products.OrderBy(p => p.Name).FirstAsync()).Id;

        var bundle = new Product
        {
            Id = Guid.NewGuid(),
            Name = "Combo",
            BasePrice = 20m,
            Type = ProductType.Menu,
            IsActive = true,
            IsAvailable = true,
            Ingredients = new List<string>(),
            Allergens = new List<string>(),
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test"
        };
        _bundleId = bundle.Id;

        var menuDefinition = new MenuDefinition
        {
            ProductId = bundle.Id,
            IsAlwaysAvailable = true,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test"
        };

        // TWO sections, and the first carries an item. One section would still detect the wipe, but
        // two also catch a fix that preserves only the first, and the item proves the cascade the
        // RemoveRange drags with it.
        var main = new MenuSection
        {
            Name = "Main",
            DisplayOrder = 0,
            IsRequired = true,
            MinSelection = 1,
            MaxSelection = 1,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test"
        };
        main.Items.Add(new MenuSectionItem
        {
            ProductId = _componentProductId,
            AdditionalPrice = 0m,
            DisplayOrder = 0,
            IsDefault = true,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test"
        });

        menuDefinition.Sections.Add(main);
        menuDefinition.Sections.Add(new MenuSection
        {
            Name = "Drink",
            DisplayOrder = 1,
            IsRequired = false,
            MinSelection = 0,
            MaxSelection = 1,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test"
        });

        bundle.MenuDefinition = menuDefinition;
        context.Products.Add(bundle);
        await context.SaveChangesAsync();
    }

    /// <summary>
    /// Sections of the bundle, in display order, WITH their item counts. The counts are not
    /// decoration: without them, deleting the `context.MenuSectionItems.Add(...)` in
    /// MenuSectionWriter.AddSections leaves every assertion here green.
    /// </summary>
    private Task<List<(string Name, int ItemCount)>> ReadSectionsAsync() => ReadSectionsAsync(_bundleId);

    private async Task<List<(string Name, int ItemCount)>> ReadSectionsAsync(Guid productId)
    {
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var definition = await context.MenuDefinitions
            .Include(d => d.Sections)
                .ThenInclude(s => s.Items)
            .AsNoTracking()
            .FirstAsync(d => d.ProductId == productId);

        return definition.Sections
            .OrderBy(s => s.DisplayOrder)
            .Select(s => (s.Name, s.Items.Count))
            .ToList();
    }

    private async Task<List<string>> ReadSectionNamesAsync() =>
        (await ReadSectionsAsync()).Select(s => s.Name).ToList();

    /// <summary>
    /// A bundle PUT body. `sections` is passed as an object so a caller can send a real array, an
    /// empty one, or omit the key — note that `null` here OMITS it rather than sending JSON null,
    /// because IntegrationTestBase.JsonOptions sets
    /// <c>DefaultIgnoreCondition = WhenWritingNull</c>. The explicit-null case therefore has to go
    /// through raw JSON (see NullSections_IsRejected_AndSectionsSurvive).
    /// </summary>
    private object BundlePayload(object? sections) => new
    {
        id = _bundleId,
        name = "Combo Renamed",
        basePrice = 22m,
        isActive = true,
        isAvailable = true,
        isSpecial = false,
        preparationTimeMinutes = 15,
        displayOrder = 0,
        menuDefinition = new { isAlwaysAvailable = true, sections },
        content = new Dictionary<string, object>()
    };

    /// <summary>
    /// A product PUT body for the same bundle. `detailedIngredients` is NOT decoration: the whole
    /// menu-definition branch of UpdateProductCommandHandler sits INSIDE
    /// <c>if (command.DetailedIngredients != null)</c>, so a payload that omits it never reaches
    /// the section code at all — a "sections preserved" assertion would then pass without the fix
    /// being involved. The admin editor always sends the array (possibly empty), which is why the
    /// wipe was reachable in the first place. Tracked separately as #296.
    /// </summary>
    private object ProductPayload(object? sections) => new
    {
        id = _bundleId,
        name = "Combo Renamed",
        basePrice = 22m,
        isActive = true,
        isAvailable = true,
        isSpecial = false,
        preparationTimeMinutes = 15,
        type = ProductType.Menu,
        kitchenType = KitchenType.None,
        displayOrder = 0,
        categoryIds = new[] { _categoryId },
        primaryCategoryId = _categoryId,
        detailedIngredients = Array.Empty<object>(),
        menuDefinition = new { isAlwaysAvailable = true, sections }
    };

    private object[] TwoReplacementSections() =>
    [
        new { name = "Starter", displayOrder = 0, isRequired = true, minSelection = 1, maxSelection = 1, items = new[] { new { productId = _componentProductId, additionalPrice = 0m, displayOrder = 0, isDefault = true } } },
        new { name = "Dessert", displayOrder = 1, isRequired = false, minSelection = 0, maxSelection = 1, items = Array.Empty<object>() }
    ];

    private Task<HttpResponseMessage> PutRawAsync(string url, string json) =>
        Client.PutAsync(url, new StringContent(json, Encoding.UTF8, "application/json"));

    // ---- PUT /api/Menus — the handler the issue was filed against ----------------------------

    // The payload that lost data: no `sections` key at all. It used to arrive as `[]`, pass the
    // guard, and delete both sections.
    [Fact]
    public async Task OmittedSections_IsRejected_AndSectionsSurvive()
    {
        AuthenticateAsAdmin();

        var response = await Client.PutAsJsonAsync($"/api/Menus/{_bundleId}", BundlePayload(null), JsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("sections");
        (await ReadSectionNamesAsync()).Should().Equal("Main", "Drink");
    }

    // The one payload that USED to preserve. It is now rejected too — deliberately: keeping it as
    // a silent "no change" would have left the endpoint with a state whose only expression is a
    // literal the UI cannot produce.
    [Fact]
    public async Task NullSections_IsRejected_AndSectionsSurvive()
    {
        AuthenticateAsAdmin();

        var json = $$"""
        {
          "id": "{{_bundleId}}",
          "name": "Combo Renamed",
          "basePrice": 22,
          "isActive": true,
          "isAvailable": true,
          "isSpecial": false,
          "preparationTimeMinutes": 15,
          "displayOrder": 0,
          "menuDefinition": { "isAlwaysAvailable": true, "sections": null },
          "content": {}
        }
        """;

        var response = await PutRawAsync($"/api/Menus/{_bundleId}", json);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadSectionNamesAsync()).Should().Equal("Main", "Drink");
    }

    // The capability the issue insisted on protecting: the bundle form HAS a section editor, so
    // `[]` is a real "I deleted them all" instruction and must keep clearing.
    [Fact]
    public async Task EmptySections_ClearsEverySection()
    {
        AuthenticateAsAdmin();

        var response = await Client.PutAsJsonAsync(
            $"/api/Menus/{_bundleId}", BundlePayload(Array.Empty<object>()), JsonOptions);

        response.EnsureSuccessStatusCode();
        (await ReadSectionNamesAsync()).Should().BeEmpty("an explicit empty list is the delete-all instruction");
    }

    [Fact]
    public async Task NonEmptySections_ReplacesEverySection()
    {
        AuthenticateAsAdmin();

        var response = await Client.PutAsJsonAsync(
            $"/api/Menus/{_bundleId}", BundlePayload(TwoReplacementSections()), JsonOptions);

        response.EnsureSuccessStatusCode();
        (await ReadSectionsAsync()).Should().Equal(("Starter", 1), ("Dessert", 0));
    }

    // `MenuSectionDto.Items` keeps its initializer, so this guard is the OPPOSITE of dead: STJ
    // writes a literal `"items": null` straight over the initializer (RespectNullableAnnotations
    // is off, which is the same mechanism that made `sections: null` the one preserving payload
    // before this fix), nothing validates Items, and the guard is all that stands between such a
    // body and an NRE. Accepted as "this section has no items" — pinned so a later "the guard is
    // unreachable, delete it" reading turns a 200 into a 500 loudly instead of in production.
    [Fact]
    public async Task SectionWithNullItems_IsAcceptedAsNoItems()
    {
        AuthenticateAsAdmin();

        var json = $$"""
        {
          "id": "{{_bundleId}}",
          "name": "Combo Renamed",
          "basePrice": 22,
          "isActive": true,
          "isAvailable": true,
          "isSpecial": false,
          "preparationTimeMinutes": 15,
          "displayOrder": 0,
          "menuDefinition": {
            "isAlwaysAvailable": true,
            "sections": [ { "name": "Solo", "displayOrder": 0, "items": null } ]
          },
          "content": {}
        }
        """;

        var response = await PutRawAsync($"/api/Menus/{_bundleId}", json);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ReadSectionsAsync()).Should().Equal(("Solo", 0));
    }

    // The new rule reads MenuDefinition.Sections, and MenuDefinition itself is still absent-able
    // (it is declared non-nullable but STJ leaves it null). Without the `.When()` guard on the
    // rule the validator would NRE and this would be a 500 — so this pins the guard, not just the
    // pre-existing "Menu definition is required" rule that also fires here.
    [Fact]
    public async Task OmittedMenuDefinition_Is400_NotServerError()
    {
        AuthenticateAsAdmin();

        var json = $$"""
        {
          "id": "{{_bundleId}}",
          "name": "Combo Renamed",
          "basePrice": 22,
          "isActive": true,
          "isAvailable": true,
          "isSpecial": false,
          "preparationTimeMinutes": 15,
          "displayOrder": 0,
          "content": {}
        }
        """;

        var response = await PutRawAsync($"/api/Menus/{_bundleId}", json);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ---- POST /api/Menus — same shared validator, harmless defect, kept consistent -----------

    // Absent sections never destroyed anything on a create (nothing exists yet), but the rule is
    // on the SHARED validator base, so create rejects them too. Pinned so a later "relax create,
    // it was never dangerous" change is a visible decision rather than a quiet divergence.
    [Fact]
    public async Task Create_OmittedSections_IsRejected()
    {
        AuthenticateAsAdmin();

        var response = await Client.PostAsJsonAsync("/api/Menus", new
        {
            name = "New Combo",
            basePrice = 30m,
            isActive = true,
            isAvailable = true,
            isSpecial = false,
            preparationTimeMinutes = 10,
            displayOrder = 0,
            menuDefinition = new { isAlwaysAvailable = true },
            content = new Dictionary<string, object>()
        }, JsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("sections");
    }

    // Before this test, the section-building loop ran ZERO iterations from the create path across
    // the whole suite — the only other POST /api/Menus tests send `sections: []`. It is now the sole
    // cover for MenuSectionWriter reached via create, so deleting a line from it is no longer free.
    [Fact]
    public async Task Create_WithSections_PersistsThem()
    {
        AuthenticateAsAdmin();

        var response = await Client.PostAsJsonAsync("/api/Menus", new
        {
            name = "New Combo",
            basePrice = 30m,
            isActive = true,
            isAvailable = true,
            isSpecial = false,
            preparationTimeMinutes = 10,
            displayOrder = 0,
            menuDefinition = new { isAlwaysAvailable = true, sections = TwoReplacementSections() },
            content = new Dictionary<string, object>()
        }, JsonOptions);

        response.EnsureSuccessStatusCode();

        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var created = await context.Products.AsNoTracking().SingleAsync(p => p.Name == "New Combo");

        (await ReadSectionsAsync(created.Id)).Should().Equal(("Starter", 1), ("Dessert", 0));
    }

    // ---- PUT /api/Products — the twin guard the issue body does not mention ------------------

    [Fact]
    public async Task Product_OmittedSections_IsRejected_AndSectionsSurvive()
    {
        AuthenticateAsAdmin();

        var response = await Client.PutAsJsonAsync(
            $"/api/Products/{_bundleId}", ProductPayload(null), JsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadSectionNamesAsync()).Should().Equal("Main", "Drink");
    }

    // Proves the product path actually REACHES the section code — without this the rejection test
    // above could be passing because nothing ran. Same payload, one field different.
    [Fact]
    public async Task Product_EmptySections_ClearsEverySection()
    {
        AuthenticateAsAdmin();

        var response = await Client.PutAsJsonAsync(
            $"/api/Products/{_bundleId}", ProductPayload(Array.Empty<object>()), JsonOptions);

        response.EnsureSuccessStatusCode();
        (await ReadSectionNamesAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task Product_NonEmptySections_ReplacesEverySection()
    {
        AuthenticateAsAdmin();

        var response = await Client.PutAsJsonAsync(
            $"/api/Products/{_bundleId}", ProductPayload(TwoReplacementSections()), JsonOptions);

        response.EnsureSuccessStatusCode();
        (await ReadSectionsAsync()).Should().Equal(("Starter", 1), ("Dessert", 0));
    }

    // Pins the FIRST conjunct of the product rule's .When(). Drop `x.MenuDefinition != null` and
    // this payload forces the `x.MenuDefinition!.Sections` accessor and 500s — and it is an
    // explicitly supported shape: UpdateProductCommand.MenuDefinition is nullable and the handler
    // guards on it. Nothing else in the suite sends type=Menu with no menuDefinition.
    [Fact]
    public async Task Product_MenuTypeWithoutMenuDefinition_StillSucceeds()
    {
        AuthenticateAsAdmin();

        var response = await Client.PutAsJsonAsync($"/api/Products/{_bundleId}", new
        {
            id = _bundleId,
            name = "Combo Renamed",
            basePrice = 22m,
            isActive = true,
            isAvailable = true,
            isSpecial = false,
            preparationTimeMinutes = 15,
            type = ProductType.Menu,
            kitchenType = KitchenType.None,
            displayOrder = 0,
            categoryIds = new[] { _categoryId },
            primaryCategoryId = _categoryId,
            detailedIngredients = Array.Empty<object>()
        }, JsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        // No `because` argument: Equal's params overload would read it as a third expected element.
        (await ReadSectionNamesAsync()).Should().Equal("Main", "Drink");
    }

    // Pins the SECOND conjunct. A non-Menu product carrying a menuDefinition is the type-conversion
    // payload the handler's `else if (… && command.Type != ProductType.Menu)` branch serves — it
    // discards the definition, so requiring sections on it would be a 400 for a field nothing reads.
    // The DTO doc states this exemption in prose; without this test nothing holds it.
    [Fact]
    public async Task Product_NonMenuTypeWithSectionlessMenuDefinition_StillSucceeds()
    {
        AuthenticateAsAdmin();

        var json = $$"""
        {
          "id": "{{_bundleId}}",
          "name": "Now A Plain Product",
          "basePrice": 22,
          "isActive": true,
          "isAvailable": true,
          "isSpecial": false,
          "preparationTimeMinutes": 15,
          "type": "mainItem",
          "kitchenType": "none",
          "displayOrder": 0,
          "categoryIds": ["{{_categoryId}}"],
          "primaryCategoryId": "{{_categoryId}}",
          "detailedIngredients": [],
          "menuDefinition": { "isAlwaysAvailable": true }
        }
        """;

        var response = await PutRawAsync($"/api/Products/{_bundleId}", json);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // The new product rule only binds a menu definition that is actually sent, on a Menu-type
    // product, so the ordinary product update — no menu definition at all — must not have gained a
    // 400. This is the payload every non-bundle save in the admin editor sends.
    [Fact]
    public async Task Product_WithoutMenuDefinition_StillSucceeds()
    {
        AuthenticateAsAdmin();

        var response = await Client.PutAsJsonAsync($"/api/Products/{_componentProductId}", new
        {
            id = _componentProductId,
            name = "Renamed Component",
            basePrice = 9m,
            isActive = true,
            isAvailable = true,
            isSpecial = false,
            preparationTimeMinutes = 5,
            type = ProductType.MainItem,
            kitchenType = KitchenType.None,
            displayOrder = 0,
            categoryIds = new[] { _categoryId },
            primaryCategoryId = _categoryId,
            detailedIngredients = Array.Empty<object>()
        }, JsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        // Asserting the rename landed proves the update was really applied, not merely accepted —
        // a 200 alone is also what a handler that refused the command returns, since the controller
        // wraps an ApiResponse failure in Ok().
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var product = await context.Products.AsNoTracking().FirstAsync(p => p.Id == _componentProductId);
        product.Name.Should().Be("Renamed Component");
    }
}
