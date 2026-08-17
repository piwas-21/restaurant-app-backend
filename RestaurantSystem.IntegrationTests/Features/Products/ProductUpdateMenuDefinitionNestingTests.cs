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

// Issue #296: UpdateProductCommandHandler's ENTIRE menu-definition branch sat inside
// `if (command.DetailedIngredients != null)`. Established by brace counting, not by reading the
// indentation — the indentation agreed, which is exactly why it read as intentional for so long.
//
// So `PUT /api/Products/{id}` carrying a menuDefinition but no `detailedIngredients` key returned
// 200 having silently discarded the menu half of the request: the schedule fields, the sections,
// and the else-branch that deletes an orphaned MenuDefinition when a product's type changes away
// from Menu. Nothing reported it.
//
// It was never reachable from the admin editor, and precisely why matters, because the obvious
// answer is only half of it. `submitEditProductForm` sets `detailedIngredients: cleanedIngredients`
// — always an array, empty at worst — which is what made the ORPHAN branch work. But that form also
// dispatches on `data.menuDefinition`: truthy goes to `updateMenuBundle` (PUT /api/Menus), falsy
// sends `toMenuDefinitionPayload(undefined)`, which returns undefined. So a menuDefinition never
// reaches PUT /api/Products from the browser at all, and the schedule/section half of this defect
// was unreachable for a reason that has nothing to do with detailedIngredients. It is an API
// contract defect, reachable by any other admin client — the field is nullable on the command and
// means "no ingredient instruction", not "no menu instruction".
//
// The three defect tests below therefore OMIT that key; sending it is what made the old code pass.
// The regression tests under the second banner deliberately send it.
//
// MenuDefinitionSectionsRequiredTests (#191) is the sibling suite and deliberately does the
// opposite: every payload there sends `detailedIngredients` precisely so it exercises the section
// code rather than this nesting.
[Collection("Database Lane 4")]
public class ProductUpdateMenuDefinitionNestingTests : IntegrationTestBase
{
    private Guid _bundleId;
    private Guid _categoryId;
    private Guid _componentProductId;

    public ProductUpdateMenuDefinitionNestingTests(DatabaseFixture databaseFixture)
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

        // Every one of the TEN request-carried fields here differs from what Schedules.First sends
        // (ten, not twelve: Upsert also writes UpdatedAt/UpdatedBy, but those come from the clock
        // and the audit identifier, not from the DTO, so no payload can pin them). A field seeded
        // to the value the payload happens to send would assert nothing — it would read as green
        // with the entire menu block skipped, which is the very thing these tests exist to detect.
        var menuDefinition = new MenuDefinition
        {
            ProductId = bundle.Id,
            IsAlwaysAvailable = true,
            StartTime = null,
            EndTime = null,
            AvailableMonday = true,
            AvailableTuesday = true,
            AvailableWednesday = true,
            AvailableThursday = true,
            AvailableFriday = true,
            AvailableSaturday = true,
            AvailableSunday = false,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test"
        };

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

        // One detailed ingredient, so the regression test below can prove the ingredient branch
        // still rewrites after the menu block was lifted out of it.
        bundle.DetailedIngredients.Add(new ProductIngredient
        {
            Name = "Original Ingredient",
            IsOptional = false,
            Price = 0m,
            IsIncludedInBasePrice = true,
            IsActive = true,
            DisplayOrder = 0,
            MaxQuantity = 1,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test"
        });

        context.Products.Add(bundle);
        await context.SaveChangesAsync();
    }

    private async Task<MenuDefinition?> ReadDefinitionAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        return await context.MenuDefinitions
            .Include(d => d.Sections)
                .ThenInclude(s => s.Items)
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.ProductId == _bundleId);
    }

    private async Task<List<(string Name, int ItemCount)>> ReadSectionsAsync()
    {
        var definition = await ReadDefinitionAsync();
        definition.Should().NotBeNull("the seeded bundle must still have a menu definition");

        return definition!.Sections
            .OrderBy(s => s.DisplayOrder)
            .Select(s => (s.Name, s.Items.Count))
            .ToList();
    }

    private async Task<List<string>> ReadIngredientNamesAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        return await context.ProductIngredients
            .Where(i => i.ProductId == _bundleId)
            .OrderBy(i => i.DisplayOrder)
            .Select(i => i.Name)
            .ToListAsync();
    }

    private Task<HttpResponseMessage> PutRawAsync(string json) =>
        Client.PutAsync(
            $"/api/Products/{_bundleId}",
            new StringContent(json, Encoding.UTF8, "application/json"));

    /// <summary>
    /// A menu schedule: the ten DTO-carried fields <c>MenuDefinitionWriter.Upsert</c> copies one by
    /// one. (It assigns twelve properties; <c>UpdatedAt</c>/<c>UpdatedBy</c> are the other two and
    /// come from the clock and the audit identifier, so no payload can pin them.)
    /// </summary>
    private sealed record Schedule(
        bool IsAlwaysAvailable,
        string StartTime,
        string EndTime,
        bool Monday,
        bool Tuesday,
        bool Wednesday,
        bool Thursday,
        bool Friday,
        bool Saturday,
        bool Sunday);

    /// <summary>
    /// Four schedules that give each of the EIGHT booleans <c>Upsert</c> copies a unique signature
    /// across the four PUTs, read most-significant-first (payload 1 → payload 4):
    ///
    /// <code>
    /// IsAlwaysAvailable 0001   AvailableThursday 0101
    /// AvailableMonday   0010   AvailableFriday   0110
    /// AvailableTuesday  0011   AvailableSaturday 0111
    /// AvailableWednesday 0100  AvailableSunday   1000
    /// </code>
    ///
    /// Field-by-field copying is what <c>Upsert</c> does, so cross-wiring two of those assignments
    /// (<c>AvailableMonday = dto.AvailableTuesday</c>) is the canonical defect of this extraction,
    /// and it is invisible to any single payload: two booleans that happen to share a value assert
    /// nothing about each other. Distinct signatures make every such swap observable.
    ///
    /// FOUR payloads, not three, for two reasons. Eight booleans need eight distinct codes and
    /// three observations supply only eight INCLUDING the two degenerate ones, `000` and `111` — a
    /// field holding a degenerate code cannot distinguish <c>= dto.X</c> from a hardcoded constant.
    /// Three also silently leaves <c>IsAlwaysAvailable</c> unsignatured if you only count the seven
    /// days: it is a bool in the same copy block and collides just as happily. Four codes drawn
    /// from 0001–1000 avoid both, so every one of the eight is pinned against a swap with any other
    /// AND against being replaced by a literal.
    ///
    /// The times vary across all four for the same reason — they are copied in the same block.
    /// </summary>
    private static class Schedules
    {
        public static readonly Schedule First =
            new(false, "11:30:00", "14:30:00", false, false, false, false, false, false, true);

        public static readonly Schedule Second =
            new(false, "18:00:00", "22:00:00", false, false, true, true, true, true, false);

        public static readonly Schedule Third =
            new(false, "09:15:00", "10:45:00", true, true, false, false, true, true, false);

        public static readonly Schedule Fourth =
            new(true, "06:05:00", "07:35:00", false, true, false, true, false, true, false);

        public static readonly Schedule[] All = [First, Second, Third, Fourth];
    }

    /// <summary>
    /// A Menu-type product PUT with a full menu definition and NO `detailedIngredients` key. Raw
    /// JSON rather than an anonymous object so the absence of that key is visible in the test
    /// rather than a property someone could "helpfully" add back.
    /// </summary>
    private string MenuPayloadWithoutDetailedIngredients(Schedule? schedule = null) =>
        BuildMenuPayload(schedule ?? Schedules.First);

    private string BuildMenuPayload(Schedule s) => $$"""
    {
      "id": "{{_bundleId}}",
      "name": "Combo Renamed",
      "basePrice": 22,
      "isActive": true,
      "isAvailable": true,
      "isSpecial": false,
      "preparationTimeMinutes": 15,
      "type": "menu",
      "kitchenType": "none",
      "displayOrder": 0,
      "categoryIds": ["{{_categoryId}}"],
      "primaryCategoryId": "{{_categoryId}}",
      "menuDefinition": {
        "isAlwaysAvailable": {{s.IsAlwaysAvailable.ToString().ToLowerInvariant()}},
        "startTime": "{{s.StartTime}}",
        "endTime": "{{s.EndTime}}",
        "availableMonday": {{s.Monday.ToString().ToLowerInvariant()}},
        "availableTuesday": {{s.Tuesday.ToString().ToLowerInvariant()}},
        "availableWednesday": {{s.Wednesday.ToString().ToLowerInvariant()}},
        "availableThursday": {{s.Thursday.ToString().ToLowerInvariant()}},
        "availableFriday": {{s.Friday.ToString().ToLowerInvariant()}},
        "availableSaturday": {{s.Saturday.ToString().ToLowerInvariant()}},
        "availableSunday": {{s.Sunday.ToString().ToLowerInvariant()}},
        "sections": [
          {
            "name": "Starter",
            "displayOrder": 0,
            "isRequired": true,
            "minSelection": 1,
            "maxSelection": 1,
            "items": [
              { "productId": "{{_componentProductId}}", "additionalPrice": 0, "displayOrder": 0, "isDefault": true }
            ]
          },
          {
            "name": "Dessert",
            "displayOrder": 1,
            "isRequired": false,
            "minSelection": 0,
            "maxSelection": 1,
            "items": []
          }
        ]
      }
    }
    """;

    // ---- The defect: the menu half of the request was silently discarded -----------------------

    // Every schedule field, not just one: the block assigns them together, and asserting a single
    // flag would stay green against a partial fix that lifted only part of it out.
    //
    // Driven four times because this is also the ONLY test of MenuDefinitionWriter.Upsert's
    // field-by-field mapping — see Schedules for why fewer payloads cannot pin it.
    [Fact]
    public async Task MenuDefinitionWithoutDetailedIngredients_UpdatesEveryScheduleField()
    {
        AuthenticateAsAdmin();

        foreach (var schedule in Schedules.All)
        {
            var response = await PutRawAsync(MenuPayloadWithoutDetailedIngredients(schedule));

            response.StatusCode.Should().Be(HttpStatusCode.OK);

            var definition = await ReadDefinitionAsync();
            definition.Should().NotBeNull();
            definition!.IsAlwaysAvailable.Should().Be(schedule.IsAlwaysAvailable);
            definition.StartTime.Should().Be(TimeSpan.Parse(schedule.StartTime));
            definition.EndTime.Should().Be(TimeSpan.Parse(schedule.EndTime));
            definition.AvailableMonday.Should().Be(schedule.Monday);
            definition.AvailableTuesday.Should().Be(schedule.Tuesday);
            definition.AvailableWednesday.Should().Be(schedule.Wednesday);
            definition.AvailableThursday.Should().Be(schedule.Thursday);
            definition.AvailableFriday.Should().Be(schedule.Friday);
            definition.AvailableSaturday.Should().Be(schedule.Saturday);
            definition.AvailableSunday.Should().Be(schedule.Sunday);
        }
    }

    // The same request's sections. Item counts are asserted too: without them, a fix that reached
    // MenuSectionWriter but dropped its item loop would still read as green.
    [Fact]
    public async Task MenuDefinitionWithoutDetailedIngredients_ReplacesSections()
    {
        AuthenticateAsAdmin();

        var response = await PutRawAsync(MenuPayloadWithoutDetailedIngredients());

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ReadSectionsAsync()).Should().Equal(("Starter", 1), ("Dessert", 0));
    }

    // The orphan. A Menu → non-Menu type change with no `detailedIngredients` never reached the
    // else-branch, so the MenuDefinition row (and its sections) outlived the product's menu-ness.
    [Fact]
    public async Task MenuToNonMenuWithoutDetailedIngredients_RemovesOrphanedDefinition()
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
          "primaryCategoryId": "{{_categoryId}}"
        }
        """;

        var response = await PutRawAsync(json);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ReadDefinitionAsync()).Should().BeNull(
            "a product that is no longer a Menu must not keep a menu definition");
    }

    // Upsert's CREATE branch, which nothing else in the suite reaches: every other seeded product
    // that meets either PUT handler already owns a MenuDefinition, and CreateMenuBundleCommand
    // builds its own inline rather than calling Upsert. So this is the only coverage of the
    // non-Menu → Menu conversion, where the definition has to be created and Added by the same
    // shared helper both handlers now depend on.
    //
    // Sections carry no items deliberately: the component product is the only other product seeded
    // here, and a menu whose one section offers the product it was converted from is a confusing
    // fixture. The item path is covered by the section tests above.
    [Fact]
    public async Task NonMenuProductGainingMenuDefinition_CreatesIt()
    {
        AuthenticateAsAdmin();

        var json = $$"""
        {
          "id": "{{_componentProductId}}",
          "name": "Promoted To Menu",
          "basePrice": 30,
          "isActive": true,
          "isAvailable": true,
          "isSpecial": false,
          "preparationTimeMinutes": 20,
          "type": "menu",
          "kitchenType": "none",
          "displayOrder": 0,
          "categoryIds": ["{{_categoryId}}"],
          "primaryCategoryId": "{{_categoryId}}",
          "menuDefinition": {
            "isAlwaysAvailable": false,
            "startTime": "12:00:00",
            "endTime": "15:00:00",
            "availableMonday": true,
            "availableTuesday": false,
            "availableWednesday": true,
            "availableThursday": false,
            "availableFriday": true,
            "availableSaturday": false,
            "availableSunday": true,
            "sections": [
              { "name": "Only Section", "displayOrder": 0, "isRequired": true, "minSelection": 1, "maxSelection": 1, "items": [] }
            ]
          }
        }
        """;

        var response = await Client.PutAsync(
            $"/api/Products/{_componentProductId}",
            new StringContent(json, Encoding.UTF8, "application/json"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var created = await context.MenuDefinitions
            .Include(d => d.Sections)
            .AsNoTracking()
            .SingleOrDefaultAsync(d => d.ProductId == _componentProductId);

        // SingleOrDefault, not FirstOrDefault: a create branch that Added a second definition
        // alongside an existing one is exactly the defect this covers, and First would hide it.
        created.Should().NotBeNull("the conversion must create the definition, not skip it");
        created!.IsAlwaysAvailable.Should().BeFalse();
        created.StartTime.Should().Be(new TimeSpan(12, 0, 0));
        created.AvailableTuesday.Should().BeFalse();
        created.AvailableSunday.Should().BeTrue();
        created.Sections.Select(s => s.Name).Should().Equal("Only Section");
    }

    // ---- Regressions: the paths that already worked must keep working -------------------------

    // Both keys present — the shape the nesting made work by accident, which lifting the block out
    // must not have disturbed. NOT "the admin editor's payload": as the header explains, the editor
    // sends a menuDefinition to PUT /api/Menus, never to this endpoint. This is an API client's
    // shape, and the closest thing to a before-and-after control the suite has.
    [Fact]
    public async Task MenuDefinitionWithDetailedIngredients_StillUpdatesScheduleAndSections()
    {
        AuthenticateAsAdmin();

        var json = MenuPayloadWithoutDetailedIngredients()
            .Replace("\"menuDefinition\":", "\"detailedIngredients\": [], \"menuDefinition\":", StringComparison.Ordinal);
        json.Should().Contain("detailedIngredients", "the regression payload must actually carry the key");

        var response = await PutRawAsync(json);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var definition = await ReadDefinitionAsync();
        definition.Should().NotBeNull();
        definition!.IsAlwaysAvailable.Should().BeFalse();
        (await ReadSectionsAsync()).Should().Equal(("Starter", 1), ("Dessert", 0));
    }

    // The other half of the un-nesting: the ingredient rewrite is now the branch's only tenant, so
    // pin that it still runs. Seeded with "Original Ingredient"; this replaces it.
    [Fact]
    public async Task DetailedIngredients_AreStillRewritten()
    {
        AuthenticateAsAdmin();

        var json = MenuPayloadWithoutDetailedIngredients().Replace(
            "\"menuDefinition\":",
            """
            "detailedIngredients": [
              { "name": "Replacement Ingredient", "isOptional": false, "price": 0, "isIncludedInBasePrice": true, "isActive": true, "displayOrder": 0, "maxQuantity": 1 }
            ], "menuDefinition":
            """,
            StringComparison.Ordinal);

        var response = await PutRawAsync(json);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ReadIngredientNamesAsync()).Should().Equal("Replacement Ingredient");
    }

    // An omitted `sections` key on this same no-detailedIngredients payload was ALREADY a 400
    // before the fix, because UpdateProductCommandValidator's rule never carried the
    // detailed-ingredients condition — it was deliberately wider than the code it protected. The
    // two now cover the same payloads; this pins that the rule did not regress into the narrower
    // shape while they were being aligned.
    [Fact]
    public async Task OmittedSectionsWithoutDetailedIngredients_IsStillRejected()
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
          "type": "menu",
          "kitchenType": "none",
          "displayOrder": 0,
          "categoryIds": ["{{_categoryId}}"],
          "primaryCategoryId": "{{_categoryId}}",
          "menuDefinition": { "isAlwaysAvailable": false }
        }
        """;

        var response = await PutRawAsync(json);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadSectionsAsync()).Should().Equal(("Main", 1), ("Drink", 0));
    }
}
