using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.IntegrationTests.Infrastructure;

public sealed class CatalogOfferFamilySchemaTests
{
    [Fact]
    public void Model_contains_offer_links_variation_items_and_presentation_mode()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=localhost;Database=not-used;Username=not-used;Password=not-used")
            .Options;
        using var context = new ApplicationDbContext(options);

        var menuDefinition = context.Model.FindEntityType(typeof(MenuDefinition));
        menuDefinition.Should().NotBeNull();
        menuDefinition!.FindProperty(nameof(MenuDefinition.ParentOfferProductId))!.ClrType
            .Should().Be<Guid?>();
        menuDefinition.FindProperty(nameof(MenuDefinition.ParentOfferVariationId))!.ClrType
            .Should().Be<Guid?>();

        menuDefinition.GetForeignKeys()
            .Where(foreignKey => foreignKey.Properties.Any(property =>
                property.Name is nameof(MenuDefinition.ParentOfferProductId)
                    or nameof(MenuDefinition.ParentOfferVariationId)))
            .Should().OnlyContain(foreignKey => foreignKey.DeleteBehavior == DeleteBehavior.Restrict);

        menuDefinition.GetIndexes()
            .Should().Contain(index => index.IsUnique
                && index.Properties.Select(property => property.Name).SequenceEqual(
                    new[] { nameof(MenuDefinition.ParentOfferProductId) }));
        menuDefinition.GetIndexes()
            .Should().Contain(index => index.IsUnique
                && index.Properties.Select(property => property.Name).SequenceEqual(
                    new[]
                    {
                        nameof(MenuDefinition.ParentOfferProductId),
                        nameof(MenuDefinition.ParentOfferVariationId)
                    }));

        var sectionItem = context.Model.FindEntityType(typeof(MenuSectionItem));
        sectionItem!.FindProperty(nameof(MenuSectionItem.ProductVariationId))!.ClrType
            .Should().Be<Guid?>();
        sectionItem.GetForeignKeys()
            .Should().Contain(foreignKey => foreignKey.Properties.Any(property =>
                property.Name == nameof(MenuSectionItem.ProductVariationId)
                && foreignKey.DeleteBehavior == DeleteBehavior.Restrict));

        var restaurantInfo = context.Model.FindEntityType(typeof(RestaurantInfo));
        restaurantInfo!.FindProperty(nameof(RestaurantInfo.BundlePresentationMode))!.GetDefaultValue()
            .Should().Be(BundlePresentationMode.LegacySeparate);
    }

    [Fact]
    public void Migration_adds_every_new_catalogue_column_and_constraint()
    {
        var migrations = Path.Combine(
            RepoRoot(), "RestaurantSystem.Infrastructure", "Persistence", "Migrations");
        var migration = Directory
            .EnumerateFiles(migrations, "*_CatalogOfferFamilies.cs")
            .Single();
        var text = File.ReadAllText(migration);

        text.Should().Contain("name: \"parent_offer_product_id\"");
        text.Should().Contain("name: \"parent_offer_variation_id\"");
        text.Should().Contain("name: \"product_variation_id\"");
        text.Should().Contain("name: \"bundle_presentation_mode\"");
        text.Should().Contain("ux_menu_definitions_parent_offer_product_id");
        text.Should().Contain("ux_menu_definitions_parent_offer_variation");
        text.Should().Contain("fk_menu_definitions_products_parent_offer_product_id");
        text.Should().Contain("fk_menu_definitions_productvariations_parent_offer_variation_id");
        text.Should().Contain("fk_menu_section_items_productvariations_product_variation_id");
    }

    private static string RepoRoot([System.Runtime.CompilerServices.CallerFilePath] string sourceFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sourceFile)!, "..", ".."));
}
