using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RestaurantSystem.Api.Features.Catalogue.Dtos;
using RestaurantSystem.Api.Features.Catalogue.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Catalogue;

[Collection("Database Lane 4")]
public sealed class CatalogueTemplateImportExecutorTests(DatabaseFixture databaseFixture)
    : IntegrationTestBase(databaseFixture)
{
    [Fact]
    public async Task Item_import_uses_normal_product_create_command_and_keeps_the_new_product_inactive()
    {
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var category = await context.Categories.AsNoTracking().FirstAsync();
        var executor = scope.ServiceProvider.GetRequiredService<ICatalogueTemplateImportExecutor>();
        var stateStore = scope.ServiceProvider.GetRequiredService<ICatalogueImportStateStore>();
        var categoryTemplate = Template("category", "category", new { sortOrder = 0 }, CatalogueImportItemStatus.Imported);
        categoryTemplate.LocalEntityType = "Category";
        categoryTemplate.LocalEntityId = category.Id;
        var itemRevision = Revision("item", new
        {
            category = new { templateId = "category", revision = 1 },
            suggestedIngredients = Array.Empty<object>(),
            optionSets = Array.Empty<object>(),
            sideSets = Array.Empty<object>()
        });
        var itemTemplate = Template("item", "item", itemRevision.Payload, CatalogueImportItemStatus.Pending);
        itemTemplate.RevisionJson = JsonSerializer.Serialize(itemRevision, JsonOptions);
        itemTemplate.DecisionJson = JsonSerializer.Serialize(new CatalogueImportItemDecision
        {
            Resolution = "Create",
            LocalName = $"Imported {Guid.NewGuid():N}",
            LocalPrice = 9.50m,
            LocalProductType = "MainItem",
            KitchenType = KitchenType.BackKitchen,
            Ingredients = ["Tenant confirmed ingredient"],
            Allergens = []
        }, JsonOptions);
        var session = new CatalogueImportSession
        {
            Id = Guid.NewGuid(),
            AdoptionId = Guid.NewGuid(),
            RootTemplateId = itemTemplate.TemplateId,
            RootRevision = itemTemplate.Revision,
            Locale = "en",
            IdempotencyKey = "integration-test",
            CreateNewCopy = true,
            CreatedBy = "test",
            Templates = [categoryTemplate, itemTemplate]
        };

        var batchContext = await stateStore.LoadBatchContextAsync(session, CancellationToken.None);
        var outcome = await executor.ExecuteAsync(session, itemTemplate, batchContext, CancellationToken.None);

        outcome.Status.Should().Be(CatalogueImportItemStatus.Imported);
        outcome.LocalEntityType.Should().Be("Product");
        outcome.LocalEntityId.Should().NotBeNull();
        var product = await context.Products.AsNoTracking().SingleAsync(value => value.Id == outcome.LocalEntityId);
        product.IsActive.Should().BeFalse();
        product.IsAvailable.Should().BeFalse();
        product.BasePrice.Should().Be(9.50m);
        product.Type.Should().Be(ProductType.MainItem);
        product.Ingredients.Should().ContainSingle().Which.Should().Be("Tenant confirmed ingredient");
        (await context.ProductCategories.AnyAsync(link => link.ProductId == product.Id && link.CategoryId == category.Id))
            .Should().BeTrue();
    }

    private static CatalogueImportSessionTemplate Template(
        string templateId,
        string type,
        object payload,
        CatalogueImportItemStatus status)
    {
        var revision = Revision(type, payload);
        return new CatalogueImportSessionTemplate
        {
            Id = Guid.NewGuid(),
            TemplateId = templateId,
            Revision = 1,
            Type = type,
            ContentHash = revision.ContentHash,
            RevisionJson = JsonSerializer.Serialize(revision, JsonOptions),
            IsSelected = true,
            Status = status,
            CreatedBy = "test"
        };
    }

    private static CentralCatalogueTemplateRevision Revision(string type, object payload) => new()
    {
        SchemaVersion = 1,
        TemplateId = type == "category" ? "category" : "item",
        Revision = 1,
        Type = type,
        Name = "Central item",
        SourceLocale = "en",
        Translations = [],
        Dependencies = [],
        Provenance = JsonSerializer.SerializeToElement(new { source = "test" }),
        QualityStatus = "reviewed",
        CompatibleTenantContractVersions = [1],
        Payload = JsonSerializer.SerializeToElement(payload),
        ContentHash = new string('a', 64)
    };
}
