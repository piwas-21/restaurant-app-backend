using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using RestaurantSystem.Api.Features.Catalog;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Menus;

[Collection("Database Lane 1")]
public class BundleNoneOptionProjectionTests(DatabaseFixture databaseFixture) : IntegrationTestBase(databaseFixture)
{
    private Guid _bundleId;

    [Theory]
    [InlineData("product")]
    [InlineData("bundle")]
    [InlineData("list")]
    public async Task Bundle_recipe_carries_the_library_answer_marker_on_every_public_read(string read)
    {
        var path = read switch
        {
            "product" => $"/api/Products/{_bundleId}",
            "bundle" => $"/api/Menus/{_bundleId}",
            _ => "/api/Menus?page=1&pageSize=100"
        };
        var response = await Client.GetAsync(path);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        body.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();
        var data = body.RootElement.GetProperty("data");
        var bundle = read == "list"
            ? data.GetProperty("items").EnumerateArray().Single(row => row.GetProperty("id").GetGuid() == _bundleId)
            : data;
        var ingredients = bundle.GetProperty("menuDefinition").GetProperty("sections")
            .EnumerateArray().Single().GetProperty("items").EnumerateArray().Single()
            .GetProperty("detailedIngredients").EnumerateArray().ToArray();

        ingredients.Select(row => row.GetProperty("isNoneOption").GetBoolean())
            .Should().Equal(true, false, false, false);
        ingredients.Select(row => row.GetProperty("name").GetString())
            .Should().Equal("Library answer", "Deleted answer", "No sauce", "No sauce");
    }

    [Theory]
    [InlineData(false, true, true)]
    [InlineData(true, true, false)]
    [InlineData(false, false, false)]
    public void Loaded_library_rows_obey_the_same_soft_delete_rule(bool deleted, bool none, bool expected)
    {
        var row = new ProductIngredient
        {
            GlobalIngredient = new GlobalIngredient { IsDeleted = deleted, IsNoneOption = none, CreatedBy = "test" },
            CreatedBy = "test"
        };
        IngredientAnswerProjection.IsNoneOption(row).Should().Be(expected);
        IngredientAnswerProjection.IsNoneOption(new ProductIngredient { CreatedBy = "test" }).Should().BeFalse();
    }

    protected override async Task SeedTestData()
    {
        await base.SeedTestData();
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var component = new Product
        {
            Name = "Bundle recipe",
            BasePrice = 10m,
            Type = ProductType.MainItem,
            IsActive = true,
            IsAvailable = true,
            CreatedBy = "test"
        };
        var live = Library("Library answer", true);
        var deleted = Library("Deleted answer", true);
        deleted.IsDeleted = true;
        var ordinary = Library("No sauce", false);
        component.DetailedIngredients.Add(Ingredient(live.DefaultName, 1, live));
        component.DetailedIngredients.Add(Ingredient(deleted.DefaultName, 2, deleted));
        component.DetailedIngredients.Add(Ingredient(ordinary.DefaultName, 3, ordinary));
        component.DetailedIngredients.Add(Ingredient("No sauce", 4, null));

        var bundle = new Product
        {
            Name = "Bundle none-option contract",
            BasePrice = 18m,
            Type = ProductType.Menu,
            DisplayOrder = -1000,
            IsActive = true,
            IsAvailable = true,
            CreatedBy = "test",
            MenuDefinition = new MenuDefinition { IsAlwaysAvailable = true, CreatedBy = "test" }
        };
        var section = new MenuSection
        {
            Name = "Dish",
            IsRequired = true,
            MinSelection = 1,
            MaxSelection = 1,
            CreatedBy = "test"
        };
        section.Items.Add(new MenuSectionItem { Product = component, CreatedBy = "test" });
        bundle.MenuDefinition.Sections.Add(section);
        context.Products.Add(bundle);
        await context.SaveChangesAsync();
        _bundleId = bundle.Id;
    }

    private static GlobalIngredient Library(string name, bool none) => new()
    {
        DefaultName = name,
        IsNoneOption = none,
        IsActive = true,
        CreatedBy = "test"
    };

    private static ProductIngredient Ingredient(string name, int order, GlobalIngredient? library) => new()
    {
        Name = name,
        DisplayOrder = order,
        GlobalIngredient = library,
        Kind = IngredientKind.Sauce,
        IsOptional = true,
        IsActive = true,
        MaxQuantity = 1,
        CreatedBy = "test"
    };
}
