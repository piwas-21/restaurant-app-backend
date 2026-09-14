using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.IntegrationTests.Infrastructure;

/// <summary>Relational guarantees for the opt-in product customization-group schema.</summary>
[Collection("Database Lane 2")]
public class ProductCustomizationGroupSchemaTests : IntegrationTestBase
{
    public ProductCustomizationGroupSchemaTests(DatabaseFixture databaseFixture)
        : base(databaseFixture)
    {
    }

    [Fact]
    public async Task A_group_round_trips_both_explicit_option_kinds()
    {
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var owner = NewProduct("Tacos");
        var ingredient = NewIngredient(owner, "Cheddar");
        var meat = NewProduct("Kebab component", isComponent: true);
        var group = NewGroup(owner, "Options", min: 1, max: 2);
        group.Descriptions.Add(new ProductCustomizationGroupDescription
        {
            LanguageCode = "fr",
            Name = "Options",
            CreatedBy = "test",
        });
        group.IngredientOptions.Add(new ProductCustomizationIngredientOption
        {
            ProductIngredient = ingredient,
            DisplayOrder = 0,
            CreatedBy = "test",
        });
        group.ProductOptions.Add(new ProductCustomizationProductOption
        {
            OptionProduct = meat,
            AdditionalPrice = 3m,
            DisplayOrder = 1,
            CreatedBy = "test",
        });
        context.Add(group);

        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        var stored = await context.ProductCustomizationGroups
            .Include(candidate => candidate.Descriptions)
            .Include(candidate => candidate.IngredientOptions)
            .Include(candidate => candidate.ProductOptions)
            .SingleAsync(candidate => candidate.Id == group.Id);

        stored.ProductId.Should().Be(owner.Id);
        stored.Descriptions.Should().ContainSingle(description => description.LanguageCode == "fr");
        stored.IngredientOptions.Should().ContainSingle(option => option.ProductIngredientId == ingredient.Id);
        stored.ProductOptions.Should().ContainSingle(option =>
            option.OptionProductId == meat.Id && option.AdditionalPrice == 3m);
    }

    [Fact]
    public async Task The_database_refuses_an_impossible_selection_range()
    {
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        context.Add(NewGroup(NewProduct("Invalid range"), "Meat", min: 3, max: 2));

        var act = async () => await context.SaveChangesAsync();

        await act.Should().ThrowAsync<DbUpdateException>(
            "a future write path must not be able to bypass the min/max invariant");
    }

    [Fact]
    public async Task One_ingredient_cannot_belong_to_two_explicit_groups()
    {
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var owner = NewProduct("One membership");
        var ingredient = NewIngredient(owner, "Cheddar");
        var first = NewGroup(owner, "Cheese A", min: 0, max: 1, displayOrder: 0);
        var second = NewGroup(owner, "Cheese B", min: 0, max: 1, displayOrder: 1);
        first.IngredientOptions.Add(NewIngredientOption(ingredient));
        second.IngredientOptions.Add(NewIngredientOption(ingredient));
        context.AddRange(first, second);

        var act = async () => await context.SaveChangesAsync();

        await act.Should().ThrowAsync<DbUpdateException>(
            "group membership must be unambiguous when the server validates a selected ingredient id");
    }

    [Fact]
    public void The_migration_drops_every_new_table_on_rollback()
    {
        var migration = File.ReadAllText(Directory
            .EnumerateFiles(
                Path.Combine(RepoRoot(), "RestaurantSystem.Infrastructure", "Persistence", "Migrations"),
                "*_AddProductCustomizationGroups.cs")
            .Single(file => !file.EndsWith(".Designer.cs", StringComparison.Ordinal)));
        var down = migration[migration.IndexOf("protected override void Down", StringComparison.Ordinal)..];

        foreach (var table in new[]
        {
            "product_customization_group_descriptions",
            "product_customization_ingredient_options",
            "product_customization_product_options",
            "product_customization_groups",
        })
        {
            down.Should().Contain($"name: \"{table}\"");
        }

        down.Split("DropTable").Length.Should().Be(5, "all four tables need a reversible migration");
    }

    private static ProductCustomizationGroup NewGroup(
        Product owner, string name, int min, int max, int displayOrder = 0) => new()
        {
            Product = owner,
            Name = name,
            MinSelection = min,
            MaxSelection = max,
            DisplayOrder = displayOrder,
            IsRequired = min > 0,
            CreatedBy = "test",
        };

    private static ProductCustomizationIngredientOption NewIngredientOption(ProductIngredient ingredient) => new()
    {
        ProductIngredient = ingredient,
        CreatedBy = "test",
    };

    private static ProductIngredient NewIngredient(Product owner, string name) => new()
    {
        Product = owner,
        Name = name,
        IsActive = true,
        IsOptional = true,
        MaxQuantity = 1,
        CreatedBy = "test",
    };

    private static Product NewProduct(string name, bool isComponent = false) => new()
    {
        Name = name,
        BasePrice = 1m,
        IsActive = true,
        IsAvailable = true,
        IsComponent = isComponent,
        CreatedBy = "test",
    };

    private static string RepoRoot([System.Runtime.CompilerServices.CallerFilePath] string sourceFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sourceFile)!, "..", ".."));
}
