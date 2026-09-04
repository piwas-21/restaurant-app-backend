using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Menus;

/// <summary>
/// One bundle, two reads, one answer (backend #468). <c>GET /api/Products/{id}</c> projected a
/// bundle's option rows through the WRITE contract's <c>MenuSectionItemDto</c> — id, name, price,
/// order — while <c>GET /api/Menus/{id}</c> projected the same rows through <c>MenuBundleMapper</c>,
/// which also carries the option's recipe, its sauce rule and its allergens. A guest who opened the
/// combo by PRODUCT id therefore got a fixed dish with nothing to customize, and the child line it
/// posted carried no <c>selectedIngredientIds</c>. That is the defect class that made a whole
/// savoury carte unorderable.
/// <para>
/// Asserted ON THE WIRE, not on the deserialized DTO: a missing key deserializes to a default, so a
/// typed comparison is blind to exactly the failure this test exists for. The key SET of the option
/// row is checked explicitly, and then the whole <c>menuDefinition</c> subtree of the two responses
/// is compared as JSON.
/// </para>
/// </summary>
[Collection("Database Lane 1")]
public class BundleReadContractAgreementTests : IntegrationTestBase
{
    private const string BundleName = "#468 Contract Combo";
    private const string OptionName = "#468 Sandwich";
    private const string DeletedOptionName = "#468 Withdrawn Option";

    private Guid _bundleId;

    public BundleReadContractAgreementTests(DatabaseFixture databaseFixture) : base(databaseFixture) { }

    /// <summary>
    /// The whole point, stated once: the two reads serve byte-identical menu definitions. Compared
    /// after a positive control below, because two EMPTY subtrees are also identical.
    /// </summary>
    [Fact]
    public async Task The_two_reads_serve_the_same_menu_definition()
    {
        var fromProduct = await MenuDefinitionJsonAsync($"/api/Products/{_bundleId}");
        var fromBundle = await MenuDefinitionJsonAsync($"/api/Menus/{_bundleId}");

        fromProduct.Should().Be(fromBundle);
    }

    /// <summary>
    /// The positive control for the comparison above, and the measurement from the issue: the option
    /// row carries the seven keys it used to drop. Read off the PRODUCT response — the one that was
    /// wrong.
    /// </summary>
    [Fact]
    public async Task The_product_read_carries_the_options_recipe_sauce_rule_and_allergens()
    {
        var item = await FirstSectionItemAsync($"/api/Products/{_bundleId}");

        item.GetProperty("detailedIngredients").EnumerateArray()
            .Select(i => i.GetProperty("name").GetString())
            // In display order, with the INACTIVE row withheld — the mapper's rule, now on both reads.
            .Should().Equal("Sauce blanche", "Salade");
        item.GetProperty("sauceMin").GetInt32().Should().Be(1);
        item.GetProperty("sauceMax").GetInt32().Should().Be(3);
        item.GetProperty("sauceIncludedFree").GetInt32().Should().Be(2);
        item.GetProperty("allergens").EnumerateArray().Select(a => a.GetString())
            .Should().Equal("gluten");
        item.GetProperty("ingredients").EnumerateArray().Select(a => a.GetString())
            .Should().Equal("Sauce blanche", "Salade");
    }

    /// <summary>
    /// The key set itself. The issue measured 8 keys against 13 on the same row; asserting the SET
    /// (rather than a count) names which one went missing when this next drifts.
    /// </summary>
    [Fact]
    public async Task The_two_reads_agree_on_the_option_rows_key_set()
    {
        var fromProduct = await FirstSectionItemAsync($"/api/Products/{_bundleId}");
        var fromBundle = await FirstSectionItemAsync($"/api/Menus/{_bundleId}");

        var expected = new[]
        {
            "id", "productId", "productName", "additionalPrice", "displayOrder", "isDefault",
            "ingredients", "allergens", "detailedIngredients", "suggestedSideItems",
            "sauceMin", "sauceMax", "sauceIncludedFree"
        };

        Keys(fromProduct).Should().BeEquivalentTo(expected);
        Keys(fromBundle).Should().BeEquivalentTo(expected);
    }

    /// <summary>
    /// A soft-deleted option must be dropped by BOTH reads. It is the one rule the product read had
    /// that the shared mapper had to absorb: that read runs <c>IgnoreQueryFilters()</c>, which
    /// un-filters every include, so the soft-delete rule cannot be left to the global filter there.
    /// </summary>
    [Fact]
    public async Task Neither_read_offers_an_option_whose_product_was_deleted()
    {
        var fromProduct = await MenuDefinitionJsonAsync($"/api/Products/{_bundleId}");
        var fromBundle = await MenuDefinitionJsonAsync($"/api/Menus/{_bundleId}");

        fromProduct.Should().NotContain(DeletedOptionName);
        fromBundle.Should().NotContain(DeletedOptionName);
        fromProduct.Should().Contain(OptionName, "the live option is still offered — the filter is not a blanket one");
    }

    private async Task<string> MenuDefinitionJsonAsync(string url)
    {
        using var document = JsonDocument.Parse(await Client.GetStringAsync(url));
        return JsonSerializer.Serialize(document.RootElement.GetProperty("data").GetProperty("menuDefinition"));
    }

    private async Task<JsonElement> FirstSectionItemAsync(string url)
    {
        var document = JsonDocument.Parse(await Client.GetStringAsync(url));
        return document.RootElement
            .GetProperty("data").GetProperty("menuDefinition")
            .GetProperty("sections").EnumerateArray().First()
            .GetProperty("items").EnumerateArray().First()
            .Clone();
    }

    private static IEnumerable<string> Keys(JsonElement element) =>
        element.EnumerateObject().Select(p => p.Name);

    protected override async Task SeedTestData()
    {
        await base.SeedTestData();

        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var option = Option();
        var withdrawn = new Product
        {
            Name = DeletedOptionName,
            BasePrice = 5m,
            Type = ProductType.MainItem,
            IsActive = true,
            IsAvailable = true,
            IsDeleted = true,
            CreatedBy = "test"
        };

        var bundle = new Product
        {
            Name = BundleName,
            BasePrice = 18m,
            Type = ProductType.Menu,
            IsActive = true,
            IsAvailable = true,
            CreatedBy = "test",
            MenuDefinition = new MenuDefinition { IsAlwaysAvailable = true, CreatedBy = "test" }
        };

        var section = new MenuSection
        {
            Name = "Plat",
            DisplayOrder = 1,
            IsRequired = true,
            MinSelection = 1,
            MaxSelection = 1,
            CreatedBy = "test"
        };
        section.Items.Add(new MenuSectionItem
        {
            Product = option,
            AdditionalPrice = 2m,
            DisplayOrder = 1,
            IsDefault = true,
            CreatedBy = "test"
        });
        section.Items.Add(new MenuSectionItem
        {
            Product = withdrawn,
            AdditionalPrice = 0m,
            DisplayOrder = 2,
            IsDefault = false,
            CreatedBy = "test"
        });
        bundle.MenuDefinition.Sections.Add(section);

        context.Products.Add(bundle);
        await context.SaveChangesAsync();

        _bundleId = bundle.Id;
    }

    /// <summary>
    /// The option product the issue measured: a recipe (one row inactive, which neither read may
    /// serve), a sauce rule, allergens and a plain ingredient list.
    /// </summary>
    private static Product Option()
    {
        var option = new Product
        {
            Name = OptionName,
            BasePrice = 12m,
            Type = ProductType.MainItem,
            IsActive = true,
            IsAvailable = true,
            SauceMin = 1,
            SauceMax = 3,
            SauceIncludedFree = 2,
            Allergens = new List<string> { "gluten" },
            CreatedBy = "test"
        };

        option.DetailedIngredients.Add(new ProductIngredient
        {
            Name = "Sauce blanche",
            IsOptional = true,
            IsActive = true,
            MaxQuantity = 2,
            DisplayOrder = 1,
            Kind = IngredientKind.Sauce,
            CreatedBy = "test"
        });
        option.DetailedIngredients.Add(new ProductIngredient
        {
            Name = "Salade",
            IsOptional = true,
            IsActive = true,
            MaxQuantity = 1,
            DisplayOrder = 2,
            CreatedBy = "test"
        });
        option.DetailedIngredients.Add(new ProductIngredient
        {
            Name = "Oignons",
            IsOptional = true,
            IsActive = false,
            MaxQuantity = 1,
            DisplayOrder = 3,
            CreatedBy = "test"
        });

        return option;
    }
}
