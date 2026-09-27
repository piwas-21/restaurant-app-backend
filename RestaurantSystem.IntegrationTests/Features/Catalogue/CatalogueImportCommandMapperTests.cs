using System.Text.Json;
using FluentAssertions;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.Catalogue.Dtos;
using RestaurantSystem.Api.Features.Catalogue.Services;
using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.IntegrationTests.Features.Catalogue;

public sealed class CatalogueImportCommandMapperTests
{
    [Fact]
    public void Item_command_keeps_tenant_reviewed_fields_and_never_activates_the_imported_product()
    {
        var revision = Revision("item", new
        {
            category = new { templateId = "grill", revision = 2 },
            suggestedIngredients = Array.Empty<object>(),
            optionSets = Array.Empty<object>(),
            sideSets = Array.Empty<object>()
        });
        var decision = new CatalogueImportItemDecision
        {
            LocalName = "Tenant Kebab",
            LocalDescription = "Tenant description",
            LocalPrice = 12.5m,
            LocalProductType = "MainItem",
            KitchenType = KitchenType.BackKitchen,
            Ingredients = ["Beef", "Salt"],
            Allergens = ["Gluten"],
            AvailableOrderTypes = 3
        };

        var command = CatalogueImportCommandMapper.Item(revision, "fr", decision, Guid.NewGuid());

        command.Name.Should().Be("Tenant Kebab");
        command.Description.Should().Be("Tenant description");
        command.BasePrice.Should().Be(12.5m);
        command.IsActive.Should().BeFalse();
        command.IsAvailable.Should().BeFalse();
        command.AvailableOrderTypes.Should().Be(3);
        command.KitchenType.Should().Be(KitchenType.BackKitchen);
        command.Ingredients.Should().Equal("Beef", "Salt");
        command.Allergens.Should().Equal("Gluten");
        command.Content["tr"].Name.Should().Be("Merkez Adı");
        command.Content["en"].Name.Should().Be("Central Name");
        command.Content["fr"].Name.Should().Be("Tenant Kebab");
        command.Content["fr"].Description.Should().Be("Tenant description");
    }

    [Fact]
    public void Item_command_returns_a_client_error_for_missing_kitchen_review()
    {
        var revision = Revision("item", new { });
        var decision = new CatalogueImportItemDecision
        {
            LocalProductType = "MainItem",
            LocalPrice = 10m
        };

        var act = () => CatalogueImportCommandMapper.Item(revision, "en", decision, Guid.NewGuid());

        act.Should().Throw<BadRequestException>().WithMessage("*Kitchen type review is required*");
    }

    [Fact]
    public void Item_command_returns_a_client_error_for_an_unsupported_tenant_product_type()
    {
        var revision = Revision("item", new { });
        var decision = new CatalogueImportItemDecision
        {
            LocalProductType = "Unknown",
            KitchenType = KitchenType.BackKitchen,
            LocalPrice = 10m
        };

        var act = () => CatalogueImportCommandMapper.Item(revision, "en", decision, Guid.NewGuid());

        act.Should().Throw<BadRequestException>().WithMessage("*supported tenant product type*");
    }

    [Fact]
    public void Item_command_preserves_explicit_empty_tenant_review_lists()
    {
        var revision = Revision("item", new { });
        var decision = new CatalogueImportItemDecision
        {
            LocalPrice = 10m,
            LocalProductType = "MainItem",
            KitchenType = KitchenType.BackKitchen,
            Ingredients = [],
            Allergens = []
        };

        var command = CatalogueImportCommandMapper.Item(revision, "en", decision, Guid.NewGuid());

        command.Ingredients.Should().BeEmpty();
        command.Allergens.Should().BeEmpty();
    }

    [Theory]
    [InlineData(true, "TENANT_INGREDIENTS_REQUIRED")]
    [InlineData(false, "TENANT_ALLERGENS_REQUIRED")]
    public void Item_command_rejects_a_missing_tenant_review_list(bool missingIngredients, string errorCode)
    {
        var revision = Revision("item", new { });
        var decision = new CatalogueImportItemDecision
        {
            LocalPrice = 10m,
            LocalProductType = "MainItem",
            KitchenType = KitchenType.BackKitchen,
            Ingredients = missingIngredients ? null : [],
            Allergens = missingIngredients ? [] : null
        };

        var act = () => CatalogueImportCommandMapper.Item(revision, "en", decision, Guid.NewGuid());

        act.Should().Throw<BadRequestException>().Which.ErrorCode.Should().Be(errorCode);
    }

    [Fact]
    public void Bundle_command_preserves_an_explicit_empty_tenant_allergen_list()
    {
        var revision = Revision("bundle", new { sections = Array.Empty<object>() });
        var decision = new CatalogueImportItemDecision { LocalPrice = 10m, Allergens = [] };

        var command = CatalogueImportCommandMapper.Bundle(revision, "en", decision, [], null);

        command.Allergens.Should().BeEmpty();
    }

    [Fact]
    public void Bundle_command_rejects_a_missing_tenant_allergen_list()
    {
        var revision = Revision("bundle", new { sections = Array.Empty<object>() });
        var decision = new CatalogueImportItemDecision { LocalPrice = 10m };

        var act = () => CatalogueImportCommandMapper.Bundle(revision, "en", decision, [], null);

        act.Should().Throw<BadRequestException>().Which.ErrorCode.Should().Be("TENANT_ALLERGENS_REQUIRED");
    }

    [Fact]
    public void Ingredient_command_maps_only_supported_central_roles_and_keeps_reviewed_locale_text()
    {
        var revision = Revision("ingredient", new { suggestedOnly = true, role = "sauce" });
        var decision = new CatalogueImportItemDecision { LocalName = "Tenant sauce", Resolution = "Create" };

        var command = CatalogueImportCommandMapper.Ingredient(revision, "fr", decision);

        command.Kind.Should().Be(IngredientKind.Sauce);
        command.DefaultName.Should().Be("Merkez Adı");
        command.Translations.Should().ContainSingle(value => value.LanguageCode == "en" && value.Name == "Central Name");
        command.Translations.Should().ContainSingle(value => value.LanguageCode == "fr" && value.Name == "Tenant sauce");
    }

    [Fact]
    public void Ingredient_command_rejects_an_unmapped_role_instead_of_guessing_a_tenant_kind()
    {
        var revision = Revision("ingredient", new { suggestedOnly = true, role = "protein" });

        var act = () => CatalogueImportCommandMapper.Ingredient(revision, "en", new CatalogueImportItemDecision());

        act.Should().Throw<BadRequestException>().Which.ErrorCode.Should().Be("UNSUPPORTED_INGREDIENT_ROLE");
    }

    private static CentralCatalogueTemplateRevision Revision(string type, object payload) => new()
    {
        SchemaVersion = 1,
        TemplateId = "test-template",
        Revision = 1,
        Type = type,
        Name = "Merkez Adı",
        Description = "Açıklama",
        SourceLocale = "tr",
        Translations = new Dictionary<string, CentralCatalogueTranslation>
        {
            ["en"] = new() { Name = "Central Name", Description = "Description" },
            ["fr"] = new() { Name = "Nom central", Description = "Description française" }
        },
        LocaleFallbacks = ["en"],
        Provenance = JsonSerializer.SerializeToElement(new { source = "test" }),
        QualityStatus = "reviewed",
        CompatibleTenantContractVersions = [1],
        Payload = JsonSerializer.SerializeToElement(payload),
        ContentHash = new string('a', 64)
    };
}
