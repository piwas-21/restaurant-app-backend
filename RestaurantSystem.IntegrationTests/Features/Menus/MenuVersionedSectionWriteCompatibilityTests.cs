using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Menus.Dtos;
using RestaurantSystem.Api.Features.Products.Commands.UpdateProductCommand;
using RestaurantSystem.Api.Features.Products.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Menus;

[Collection("Database Lane 1")]
public sealed class MenuVersionedSectionWriteCompatibilityTests : IntegrationTestBase
{
    private const string Actor = "versioned-section-compat-test";
    private const string OriginalSectionName = "Choose a side";
    private static readonly Guid BundleId = Guid.NewGuid();
    private static readonly Guid DefinitionId = Guid.NewGuid();
    private static readonly Guid SectionId = Guid.NewGuid();
    private static readonly Guid SectionItemId = Guid.NewGuid();
    private static readonly Guid ChoiceProductId = Guid.NewGuid();
    private Guid _categoryId;

    public MenuVersionedSectionWriteCompatibilityTests(DatabaseFixture databaseFixture) : base(databaseFixture)
    {
    }

    [Fact]
    public async Task LegacyMenuPutRejectsChangedSectionsButAllowsOtherFieldsWithAnUnchangedSnapshot()
    {
        AuthenticateAsAdmin();
        (await PatchSectionNameAsync("Edited in authoring API")).StatusCode.Should().Be(HttpStatusCode.OK);

        var stalePut = await PutAsJsonAsync($"/api/Menus/{BundleId}",
            BundlePutPayload("Stale bundle name", OriginalSectionName));
        stalePut.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ReadStoredSectionAsync()).Name.Should().Be("Edited in authoring API");
        (await ReadStoredProductNameAsync()).Should().Be("Section test bundle");

        var unchangedSnapshotPut = await PutAsJsonAsync($"/api/Menus/{BundleId}",
            BundlePutPayload("Non-section edit", "Edited in authoring API"));
        unchangedSnapshotPut.StatusCode.Should().Be(HttpStatusCode.OK,
            await unchangedSnapshotPut.Content.ReadAsStringAsync());

        var persisted = await ReadStoredSectionAsync();
        persisted.Id.Should().Be(SectionId);
        persisted.Items.Single().Id.Should().Be(SectionItemId,
            "the compatibility path must skip destructive replacement when sections are unchanged");
        persisted.Name.Should().Be("Edited in authoring API");
        (await ReadVersionedEditingStartedAsync()).Should().BeTrue();
    }

    [Fact]
    public async Task LegacyProductPutRejectsChangedSectionsAfterVersionedEditingStarted()
    {
        AuthenticateAsAdmin();
        (await PatchSectionNameAsync("Edited in authoring API")).StatusCode.Should().Be(HttpStatusCode.OK);

        var stalePut = await PutAsJsonAsync($"/api/Products/{BundleId}",
            ProductPutCommand("Legacy overwrite"));

        stalePut.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var persisted = await ReadStoredSectionAsync();
        persisted.Id.Should().Be(SectionId);
        persisted.Name.Should().Be("Edited in authoring API");
        persisted.Items.Single().Id.Should().Be(SectionItemId);
        (await ReadVersionedEditingStartedAsync()).Should().BeTrue();
        (await ReadStoredProductNameAsync()).Should().Be("Section test bundle");
    }

    [Fact]
    public async Task LegacyProductPutAllowsOtherFieldsWithAnUnchangedSectionSnapshot()
    {
        AuthenticateAsAdmin();
        (await PatchSectionNameAsync("Edited in authoring API")).StatusCode.Should().Be(HttpStatusCode.OK);

        var unchangedSnapshotPut = await PutAsJsonAsync($"/api/Products/{BundleId}",
            ProductPutCommand("Edited in authoring API") with { Name = "Non-section product edit" });

        unchangedSnapshotPut.StatusCode.Should().Be(HttpStatusCode.OK,
            await unchangedSnapshotPut.Content.ReadAsStringAsync());
        var persisted = await ReadStoredSectionAsync();
        persisted.Id.Should().Be(SectionId);
        persisted.Name.Should().Be("Edited in authoring API");
        persisted.Items.Single().Id.Should().Be(SectionItemId);
        (await ReadStoredProductNameAsync()).Should().Be("Non-section product edit");
    }

    [Fact]
    public async Task ConcurrentVersionedPatchAndStaleLegacyPutPreserveThePatch()
    {
        AuthenticateAsAdmin();
        (await PatchSectionNameAsync("Edited in authoring API")).StatusCode.Should().Be(HttpStatusCode.OK);

        var responses = await Task.WhenAll(
            SendPatchAsync("Concurrent authoring edit", expectedVersion: 2),
            PutAsJsonAsync($"/api/Menus/{BundleId}",
                BundlePutPayload("Stale concurrent bundle name", OriginalSectionName)));

        responses[0].StatusCode.Should().Be(HttpStatusCode.OK,
            await responses[0].Content.ReadAsStringAsync());
        responses[1].StatusCode.Should().Be(HttpStatusCode.Conflict,
            await responses[1].Content.ReadAsStringAsync());
        var persisted = await ReadStoredDefinitionAsync();
        persisted.AuthoringVersion.Should().Be(3);
        persisted.VersionedSectionEditingStarted.Should().BeTrue();
        persisted.Sections.Single().Id.Should().Be(SectionId);
        persisted.Sections.Single().Name.Should().Be("Concurrent authoring edit");
        (await ReadStoredProductNameAsync()).Should().Be("Section test bundle");
    }

    protected override async Task SeedTestData()
    {
        await base.SeedTestData();
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var category = await context.Categories.FirstAsync();
        _categoryId = category.Id;

        var choice = NewProduct(ChoiceProductId, "Side choice", ProductType.MainItem, category, 5m);
        var bundle = NewProduct(BundleId, "Section test bundle", ProductType.Menu, category, 12m);
        var definition = new MenuDefinition
        {
            Id = DefinitionId,
            ProductId = BundleId,
            Product = bundle,
            IsAlwaysAvailable = true,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = Actor
        };
        var section = new MenuSection
        {
            Id = SectionId,
            MenuDefinitionId = DefinitionId,
            MenuDefinition = definition,
            Name = OriginalSectionName,
            Description = "Choose one side",
            DisplayOrder = 0,
            IsRequired = true,
            MinSelection = 1,
            MaxSelection = 1,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = Actor
        };
        section.Items.Add(new MenuSectionItem
        {
            Id = SectionItemId,
            MenuSectionId = SectionId,
            MenuSection = section,
            ProductId = ChoiceProductId,
            Product = choice,
            DisplayOrder = 0,
            IsDefault = true,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = Actor
        });
        definition.Sections.Add(section);
        bundle.MenuDefinition = definition;
        context.AddRange(choice, bundle, definition);
        await context.SaveChangesAsync();
    }

    private async Task<HttpResponseMessage> PatchSectionNameAsync(string name)
    {
        using var request = CreatePatchRequest(name);
        return await Client.SendAsync(request);
    }

    private Task<HttpResponseMessage> SendPatchAsync(string name, int expectedVersion = 1)
    {
        var request = CreatePatchRequest(name, expectedVersion);
        return Client.SendAsync(request);
    }

    private HttpRequestMessage CreatePatchRequest(string name, int expectedVersion = 1)
    {
        var request = new HttpRequestMessage(HttpMethod.Patch, $"/api/Menus/{BundleId}/sections")
        {
            Content = JsonContent.Create(new
            {
                sections = new[]
                {
                    new
                    {
                        id = SectionId,
                        name,
                        description = "Choose one side",
                        displayOrder = 0,
                        isRequired = true,
                        minSelection = 1,
                        maxSelection = 1
                    }
                }
            }, options: JsonOptions)
        };
        request.Headers.TryAddWithoutValidation("If-Match", $"\"{expectedVersion}\"");
        return request;
    }

    private object BundlePutPayload(string name, string sectionName) => new
    {
        id = BundleId,
        name,
        description = "Bundle description",
        basePrice = 12m,
        isActive = true,
        isAvailable = true,
        isSpecial = false,
        preparationTimeMinutes = 10,
        displayOrder = 0,
        categoryIds = new[] { _categoryId },
        primaryCategoryId = _categoryId,
        menuDefinition = new
        {
            id = DefinitionId,
            isAlwaysAvailable = true,
            availableMonday = true,
            availableTuesday = true,
            availableWednesday = true,
            availableThursday = true,
            availableFriday = true,
            availableSaturday = true,
            availableSunday = true,
            sections = new[] { SectionPayload(sectionName) }
        },
        content = new Dictionary<string, object>()
    };

    private UpdateProductCommand ProductPutCommand(string sectionName) => new(
        Id: BundleId,
        Name: "Legacy product PUT",
        Description: "Bundle description",
        BasePrice: 12m,
        IsActive: true,
        IsAvailable: true,
        IsSpecial: false,
        PreparationTimeMinutes: 10,
        Type: ProductType.Menu,
        KitchenType: KitchenType.None,
        Ingredients: [],
        Allergens: [],
        DisplayOrder: 0,
        CategoryIds: [_categoryId],
        PrimaryCategoryId: _categoryId,
        Variations: [],
        SuggestedSideItemIds: [],
        DetailedIngredients: [],
        MenuDefinition: new MenuDefinitionDto
        {
            Id = DefinitionId,
            IsAlwaysAvailable = true,
            Sections = [SectionDto(sectionName)]
        },
        Content: new ProductDescriptionsDto());

    private object SectionPayload(string name) => new
    {
        id = SectionId,
        name,
        description = "Choose one side",
        displayOrder = 0,
        isRequired = true,
        minSelection = 1,
        maxSelection = 1,
        items = new[]
        {
            new
            {
                id = SectionItemId,
                productId = ChoiceProductId,
                additionalPrice = 0m,
                displayOrder = 0,
                isDefault = true
            }
        }
    };

    private MenuSectionDto SectionDto(string name) => new()
    {
        Id = SectionId,
        Name = name,
        Description = "Choose one side",
        DisplayOrder = 0,
        IsRequired = true,
        MinSelection = 1,
        MaxSelection = 1,
        Items =
        [
            new MenuSectionItemDto
            {
                Id = SectionItemId,
                ProductId = ChoiceProductId,
                AdditionalPrice = 0m,
                DisplayOrder = 0,
                IsDefault = true
            }
        ]
    };

    private async Task<MenuSection> ReadStoredSectionAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await context.MenuSections.AsNoTracking()
            .Include(section => section.Items)
            .SingleAsync(section => section.Id == SectionId);
    }

    private async Task<bool> ReadVersionedEditingStartedAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await context.MenuDefinitions.AsNoTracking()
            .Where(definition => definition.ProductId == BundleId)
            .Select(definition => definition.VersionedSectionEditingStarted)
            .SingleAsync();
    }

    private async Task<string> ReadStoredProductNameAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await context.Products.AsNoTracking()
            .Where(product => product.Id == BundleId)
            .Select(product => product.Name)
            .SingleAsync();
    }

    private async Task<MenuDefinition> ReadStoredDefinitionAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await context.MenuDefinitions.AsNoTracking()
            .Include(definition => definition.Sections)
            .SingleAsync(definition => definition.ProductId == BundleId);
    }

    private static Product NewProduct(Guid id, string name, ProductType type, Category category, decimal price)
    {
        var product = new Product
        {
            Id = id,
            Name = name,
            BasePrice = price,
            Type = type,
            IsActive = true,
            IsAvailable = true,
            Ingredients = [],
            Allergens = [],
            CreatedAt = DateTime.UtcNow,
            CreatedBy = Actor
        };
        product.ProductCategories.Add(new ProductCategory
        {
            Product = product,
            Category = category,
            IsPrimary = true,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = Actor
        });
        return product;
    }
}
