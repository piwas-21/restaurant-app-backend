using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Conventers;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Common.TenantFeatures;
using RestaurantSystem.Api.Features.OptionSets.Dtos;
using RestaurantSystem.Api.Features.OptionSets.Materialization;
using RestaurantSystem.Api.Features.Products.Commands.UpdateProductCommand;
using RestaurantSystem.Api.Features.Products.Dtos;
using RestaurantSystem.Api.Settings;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.OptionSets;

[Collection("Database Lane 1")]
public sealed class OptionSetMaterializationTests : IntegrationTestBase
{
    private const string Actor = "option-set-materialization-test";
    private static readonly Guid MenuId = Guid.NewGuid();
    private static readonly Guid StandaloneProductId = Guid.NewGuid();
    private static readonly Guid DefinitionId = Guid.NewGuid();
    private static readonly Guid SectionId = Guid.NewGuid();
    private static readonly Guid ChoiceGroupId = Guid.NewGuid();
    private static readonly Guid ExistingChoiceId = Guid.NewGuid();
    private static readonly Guid AddedChoiceId = Guid.NewGuid();
    private static readonly Guid ExistingSectionItemId = Guid.NewGuid();
    private static readonly Guid ExistingProductChoiceId = Guid.NewGuid();
    private Guid _categoryId;

    public OptionSetMaterializationTests(DatabaseFixture databaseFixture) : base(databaseFixture) { }

    protected override void ConfigureTestServices(IServiceCollection services) =>
        services.PostConfigure<TenantFeatureSettings>(settings => settings.OptionSetMaterializationEnabled = true);

    [Fact]
    public async Task Materialization_is_disabled_by_default_until_tenant_rollout_is_enabled()
    {
        await using var context = DatabaseFixture.CreateContext();
        var caller = new Mock<ICurrentUserService>();
        var catalog = new Mock<RestaurantSystem.Api.Features.OptionSets.Services.IOptionSetCatalogService>();
        var features = new TenantFeatures(Options.Create(new TenantFeatureSettings()));
        var materializer = new OptionSetMaterializer(context, caller.Object, catalog.Object, features);

        var action = () => materializer.ApplyAsync(new OptionSetMaterializationRequest(), CancellationToken.None);

        await action.Should().ThrowAsync<OptionSetMaterializationDisabledException>();
    }

    [Fact]
    public async Task Apply_preserves_rows_is_idempotent_and_protects_both_legacy_menu_puts()
    {
        AuthenticateAsAdmin();
        var set = await CreateBundleChoiceSetAsync();
        var setData = set!.Data!;
        setData.SourceLocale.Should().Be("fr");
        setData.Translations.Should().BeEquivalentTo(new Dictionary<string, string>
        {
            ["en"] = "Taco proteins",
            ["fr"] = "Protéines du taco"
        });
        var request = MaterializationRequest(setData.Id);

        var previewResponse = await PostAsJsonAsync($"/api/OptionSets/{setData.Id}/preview", request);
        previewResponse.StatusCode.Should().Be(HttpStatusCode.OK,
            await previewResponse.Content.ReadAsStringAsync());
        var preview = await ReadResponseAsync<ApiResponse<OptionSetMaterializationPreview>>(previewResponse);
        preview!.Data!.Targets.Should().ContainSingle().Which.Status.Should().Be("ready");

        var applyResponse = await PostAsJsonAsync($"/api/OptionSets/{setData.Id}/apply", request);
        applyResponse.StatusCode.Should().Be(HttpStatusCode.OK,
            await applyResponse.Content.ReadAsStringAsync());
        var applied = await ReadResponseAsync<ApiResponse<OptionSetMaterializationResult>>(applyResponse);
        var target = applied!.Data!.Targets.Should().ContainSingle().Which;
        target.Status.Should().Be("applied");
        target.MenuAuthoringVersion.Should().Be(2);
        target.AttachmentVersion.Should().Be(1);
        target.AppliedRows.Should().HaveCount(2);
        target.AppliedRows.Should().Contain(row => row.RowId == ExistingSectionItemId && row.Action == "preserve");

        var retry = await PostAsJsonAsync($"/api/OptionSets/{setData.Id}/apply", request);
        retry.StatusCode.Should().Be(HttpStatusCode.OK,
            await retry.Content.ReadAsStringAsync());
        var retried = await ReadResponseAsync<ApiResponse<OptionSetMaterializationResult>>(retry);
        retried!.Data!.Targets.Should().ContainSingle().Which.Status.Should().Be("unchanged");

        await using (var context = DatabaseFixture.CreateContext())
        {
            var menu = await context.MenuDefinitions.Include(item => item.Sections)
                .ThenInclude(section => section.Items)
                .SingleAsync(item => item.ProductId == MenuId);
            menu.VersionedSectionEditingStarted.Should().BeTrue();
            menu.AuthoringVersion.Should().Be(2);
            var section = menu.Sections.Single();
            section.Items.Should().HaveCount(2);
            section.Items.Should().Contain(item => item.Id == ExistingSectionItemId);
            (await context.OptionSetAppliedRows.CountAsync()).Should().Be(2);
        }

        var menuPut = await PutAsJsonAsync($"/api/Menus/{MenuId}", MenuPutPayload("Legacy replacement"));
        menuPut.StatusCode.Should().Be(HttpStatusCode.Conflict,
            "the menu endpoint must not replace sections after option-set materialization");

        var productPut = await PutAsJsonAsync($"/api/Products/{MenuId}", ProductPutCommand("Legacy replacement"));
        productPut.StatusCode.Should().Be(HttpStatusCode.Conflict,
            $"the Menu-type product endpoint must enforce the same section watermark: {await productPut.Content.ReadAsStringAsync()}");
    }

    [Fact]
    public async Task Update_requires_current_option_set_etag_and_preserves_entry_ids()
    {
        AuthenticateAsAdmin();
        var created = await CreateBundleChoiceSetAsync();
        var detail = created!.Data!;
        var firstId = detail.Entries[0].Id;
        var update = new OptionSetWriteRequestDto
        {
            Kind = detail.Kind,
            Name = "Taco proteins renamed",
            SourceLocale = detail.SourceLocale,
            Translations = detail.Translations,
            Entries = detail.Entries
        };

        var missingTag = await PutAsJsonAsync($"/api/OptionSets/{detail.Id}", update);
        missingTag.StatusCode.Should().Be(HttpStatusCode.PreconditionRequired);

        Client.DefaultRequestHeaders.TryAddWithoutValidation("If-Match", "\"1\"");
        var response = await PutAsJsonAsync($"/api/OptionSets/{detail.Id}", update);
        response.StatusCode.Should().Be(HttpStatusCode.OK,
            await response.Content.ReadAsStringAsync());
        var saved = await ReadResponseAsync<ApiResponse<OptionSetDetailDto>>(response);
        saved!.Data!.Version.Should().Be(2);
        saved.Data.Entries.Should().HaveCount(2);
        saved.Data.Entries[0].Id.Should().Be(firstId);
    }

    [Fact]
    public async Task One_set_materializes_to_linked_standalone_and_menu_choices_with_stable_ids()
    {
        AuthenticateAsAdmin();
        var set = (await CreateBundleChoiceSetAsync())!.Data!;
        var standaloneOnly = new OptionSetMaterializationRequest
        {
            OptionSetId = set.Id,
            ExpectedSetVersion = set.Version,
            IdempotencyKey = $"standalone-only-{Guid.NewGuid():N}",
            Targets =
            [
                new OptionSetMaterializationTargetRequest
                {
                    TargetKey = "taco-standalone-choice-only",
                    Role = OptionSetAttachmentRole.ProductChoice,
                    TargetProductId = StandaloneProductId,
                    TargetCustomizationGroupId = ChoiceGroupId,
                    ExpectedCustomizationGroupVersion = 1,
                    Settings = new OptionSetAttachmentSettings { MinSelection = 1, MaxSelection = 2 }
                }
            ]
        };
        var differencePreviewResponse = await PostAsJsonAsync($"/api/OptionSets/{set.Id}/preview", standaloneOnly);
        var differencePreview = await ReadResponseAsync<ApiResponse<OptionSetMaterializationPreview>>(differencePreviewResponse);
        differencePreview!.Data!.RelatedOfferWarnings.Should().ContainSingle(warning =>
            warning.RelatedProductId == MenuId && warning.RelatedTargetRole == "bundleChoice" && warning.ReasonRequired);
        var unreasonedApply = await PostAsJsonAsync($"/api/OptionSets/{set.Id}/apply", standaloneOnly);
        unreasonedApply.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var request = new OptionSetMaterializationRequest
        {
            OptionSetId = set.Id,
            ExpectedSetVersion = set.Version,
            IdempotencyKey = $"linked-offer-{Guid.NewGuid():N}",
            Targets =
            [
                new OptionSetMaterializationTargetRequest
                {
                    TargetKey = "taco-standalone-choice",
                    Role = OptionSetAttachmentRole.ProductChoice,
                    TargetProductId = StandaloneProductId,
                    TargetCustomizationGroupId = ChoiceGroupId,
                    ExpectedCustomizationGroupVersion = 1,
                    Settings = new OptionSetAttachmentSettings
                    {
                        MinSelection = 1, MaxSelection = 2, IncludedFree = 0, DisplayOrder = 0
                    }
                },
                new OptionSetMaterializationTargetRequest
                {
                    TargetKey = "taco-menu-choice",
                    Role = OptionSetAttachmentRole.BundleChoice,
                    TargetProductId = MenuId,
                    TargetMenuSectionId = SectionId,
                    ExpectedMenuAuthoringVersion = 1,
                    Settings = new OptionSetAttachmentSettings { MinSelection = 1, MaxSelection = 2, DisplayOrder = 0 }
                }
            ]
        };

        var previewResponse = await PostAsJsonAsync($"/api/OptionSets/{set.Id}/preview", request);
        previewResponse.StatusCode.Should().Be(HttpStatusCode.OK,
            await previewResponse.Content.ReadAsStringAsync());
        var preview = await ReadResponseAsync<ApiResponse<OptionSetMaterializationPreview>>(previewResponse);
        preview!.Data!.RelatedOfferWarnings.Should().BeEmpty();
        preview.Data.Targets.Should().HaveCount(2);
        preview.Data.Targets.Single(target => target.TargetKey == "taco-standalone-choice")
            .CurrentCustomizationGroupVersion.Should().Be(1);

        var appliedResponse = await PostAsJsonAsync($"/api/OptionSets/{set.Id}/apply", request);
        appliedResponse.StatusCode.Should().Be(HttpStatusCode.OK,
            await appliedResponse.Content.ReadAsStringAsync());
        var applied = await ReadResponseAsync<ApiResponse<OptionSetMaterializationResult>>(appliedResponse);
        applied!.Data!.Targets.Should().HaveCount(2);
        var standaloneResult = applied.Data.Targets.Single(target => target.TargetKey == "taco-standalone-choice");
        standaloneResult.CustomizationGroupVersion.Should().Be(2);
        standaloneResult.AppliedRows.Should().Contain(row => row.RowId == ExistingProductChoiceId
            && row.RowType == "ProductCustomizationProductOption" && row.Action == "preserve");
        applied.Data.Targets.Single(target => target.TargetKey == "taco-menu-choice")
            .MenuAuthoringVersion.Should().Be(2);

        await using (var context = DatabaseFixture.CreateContext())
        {
            var group = await context.ProductCustomizationGroups
                .Include(item => item.ProductOptions)
                .SingleAsync(item => item.Id == ChoiceGroupId);
            group.AuthoringVersion.Should().Be(2);
            group.ProductOptions.Should().HaveCount(2);
            group.ProductOptions.Should().Contain(option => option.Id == ExistingProductChoiceId);
            group.ProductOptions.Single(option => option.Id == ExistingProductChoiceId)
                .AdditionalPrice.Should().Be(2.25m, "local surcharges must survive option-set apply");
            (await context.OptionSetAttachments.CountAsync()).Should().Be(2);
        }

        var savedProduct = await GetFromJsonAsync<ApiResponse<ProductDto>>($"/api/Products/{StandaloneProductId}");
        var savedGroup = savedProduct!.Data!.CustomizationGroups.Single();
        savedGroup.AuthoringVersion.Should().Be(2);
        var unchangedPut = await PutAsJsonAsync($"/api/Products/{StandaloneProductId}",
            ProductPutCommand(savedProduct.Data, [savedGroup]));
        unchangedPut.StatusCode.Should().Be(HttpStatusCode.OK,
            await unchangedPut.Content.ReadAsStringAsync());

        var changedGroup = savedGroup with { Name = "Replace the attached set" };
        var changedPut = await PutAsJsonAsync($"/api/Products/{StandaloneProductId}",
            ProductPutCommand(savedProduct.Data, [changedGroup]));
        changedPut.StatusCode.Should().Be(HttpStatusCode.Conflict,
            await changedPut.Content.ReadAsStringAsync());

        var staleRequest = new OptionSetMaterializationRequest
        {
            OptionSetId = set.Id,
            ExpectedSetVersion = set.Version,
            IdempotencyKey = $"stale-choice-{Guid.NewGuid():N}",
            Targets =
            [
                new OptionSetMaterializationTargetRequest
                {
                    TargetKey = "stale-taco-choice",
                    Role = OptionSetAttachmentRole.ProductChoice,
                    TargetProductId = StandaloneProductId,
                    TargetCustomizationGroupId = ChoiceGroupId,
                    ExpectedCustomizationGroupVersion = 1,
                    ExpectedAttachmentVersion = 1
                }
            ]
        };
        var stalePreviewResponse = await PostAsJsonAsync($"/api/OptionSets/{set.Id}/preview", staleRequest);
        stalePreviewResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var stalePreview = await ReadResponseAsync<ApiResponse<OptionSetMaterializationPreview>>(stalePreviewResponse);
        stalePreview!.Data!.Targets.Single().Status.Should().Be("conflict");

        var staleApplyResponse = await PostAsJsonAsync($"/api/OptionSets/{set.Id}/apply", staleRequest);
        staleApplyResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var staleApply = await ReadResponseAsync<ApiResponse<OptionSetMaterializationResult>>(staleApplyResponse);
        staleApply!.Data!.Targets.Single().Status.Should().Be("conflict");
    }

    [Fact]
    public async Task Option_set_enums_use_the_documented_wire_values()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new StringEnumConverterFactory());

        JsonSerializer.Serialize(OptionSetKind.BundleChoice, options).Should().Be("\"bundleChoice\"");
        JsonSerializer.Serialize(OptionSetAttachmentRole.ProductChoice, options).Should().Be("\"productChoice\"");
        JsonSerializer.Deserialize<OptionSetAttachmentRole>("\"productChoice\"", options)
            .Should().Be(OptionSetAttachmentRole.ProductChoice);
    }

    protected override async Task SeedTestData()
    {
        await base.SeedTestData();
        await using var context = DatabaseFixture.CreateContext();
        var category = await context.Categories.FirstAsync();
        _categoryId = category.Id;
        var existingChoice = Product(ExistingChoiceId, "Chicken", 6m);
        var addedChoice = Product(AddedChoiceId, "Falafel", 5m);
        var standaloneProduct = Product(StandaloneProductId, "Taco", 12m);
        standaloneProduct.ProductCategories.Add(new ProductCategory
        {
            ProductId = StandaloneProductId,
            CategoryId = _categoryId,
            IsPrimary = true,
            DisplayOrder = 0,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = Actor
        });
        var choiceGroup = new ProductCustomizationGroup
        {
            Id = ChoiceGroupId,
            ProductId = StandaloneProductId,
            Product = standaloneProduct,
            Name = "Choose a filling",
            DisplayOrder = 0,
            IsRequired = true,
            MinSelection = 1,
            MaxSelection = 2,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = Actor
        };
        choiceGroup.ProductOptions.Add(new ProductCustomizationProductOption
        {
            Id = ExistingProductChoiceId,
            ProductCustomizationGroupId = ChoiceGroupId,
            ProductCustomizationGroup = choiceGroup,
            OptionProductId = ExistingChoiceId,
            OptionProduct = existingChoice,
            AdditionalPrice = 2.25m,
            DisplayOrder = 0,
            IsDefault = true,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = Actor
        });
        standaloneProduct.CustomizationGroups.Add(choiceGroup);
        var menuProduct = Product(MenuId, "Taco meal", 14m);
        menuProduct.Type = ProductType.Menu;
        menuProduct.ProductCategories.Add(new ProductCategory
        {
            ProductId = MenuId,
            CategoryId = _categoryId,
            IsPrimary = true,
            DisplayOrder = 0,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = Actor
        });
        var definition = new MenuDefinition
        {
            Id = DefinitionId,
            ProductId = MenuId,
            Product = menuProduct,
            ParentOfferProductId = StandaloneProductId,
            IsAlwaysAvailable = true,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = Actor
        };
        var section = new MenuSection
        {
            Id = SectionId,
            MenuDefinitionId = DefinitionId,
            MenuDefinition = definition,
            Name = "Choose a filling",
            DisplayOrder = 0,
            IsRequired = true,
            MinSelection = 1,
            MaxSelection = 2,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = Actor
        };
        section.Items.Add(new MenuSectionItem
        {
            Id = ExistingSectionItemId,
            MenuSectionId = SectionId,
            MenuSection = section,
            ProductId = ExistingChoiceId,
            Product = existingChoice,
            DisplayOrder = 0,
            IsDefault = true,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = Actor
        });
        definition.Sections.Add(section);
        menuProduct.MenuDefinition = definition;
        context.AddRange(existingChoice, addedChoice, standaloneProduct, choiceGroup, menuProduct, definition);
        await context.SaveChangesAsync();
    }

    private async Task<ApiResponse<OptionSetDetailDto>?> CreateBundleChoiceSetAsync()
    {
        var request = new OptionSetWriteRequestDto
        {
            Kind = OptionSetKind.BundleChoice,
            Name = "Taco proteins",
            SourceLocale = "fr",
            Translations = new Dictionary<string, string>
            {
                ["en"] = "Taco proteins",
                ["fr"] = "Protéines du taco"
            },
            Entries =
            [
                new OptionSetEntryDto { Name = "Chicken", ProductId = ExistingChoiceId, IsDefault = true },
                new OptionSetEntryDto { Name = "Falafel", ProductId = AddedChoiceId, DisplayOrder = 1 }
            ]
        };
        var response = await PostAsJsonAsync("/api/OptionSets", request);
        response.StatusCode.Should().Be(HttpStatusCode.OK,
            await response.Content.ReadAsStringAsync());
        (await response.Content.ReadAsStringAsync()).Should().Contain("\"kind\":\"bundleChoice\"");
        return await ReadResponseAsync<ApiResponse<OptionSetDetailDto>>(response);
    }

    private static OptionSetMaterializationRequest MaterializationRequest(Guid setId) => new()
    {
        OptionSetId = setId,
        ExpectedSetVersion = 1,
        IdempotencyKey = $"materialize-{Guid.NewGuid():N}",
        Targets =
        [
            new OptionSetMaterializationTargetRequest
            {
                TargetKey = "taco-meal-proteins",
                Role = OptionSetAttachmentRole.BundleChoice,
                TargetProductId = MenuId,
                TargetMenuSectionId = SectionId,
                ExpectedMenuAuthoringVersion = 1,
                IntentionalDifferenceReason = "The standalone product keeps its existing choice group.",
                Settings = new OptionSetAttachmentSettings { MinSelection = 1, MaxSelection = 2, DisplayOrder = 0 }
            }
        ]
    };

    private object MenuPutPayload(string sectionName) => new
    {
        id = MenuId,
        name = "Taco meal",
        description = "Bundle description",
        basePrice = 14m,
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
            sections = new[]
            {
                new
                {
                    id = SectionId,
                    name = sectionName,
                    displayOrder = 0,
                    isRequired = true,
                    minSelection = 1,
                    maxSelection = 2,
                    items = new[]
                    {
                        new { productId = ExistingChoiceId, displayOrder = 0, isDefault = true },
                        new { productId = AddedChoiceId, displayOrder = 1, isDefault = false }
                    }
                }
            }
        },
        content = new Dictionary<string, object>()
    };

    private UpdateProductCommand ProductPutCommand(string sectionName) => new(
        Id: MenuId,
        Name: "Taco meal",
        Description: "Bundle description",
        BasePrice: 14m,
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
            Sections =
            [
                new MenuSectionDto
                {
                    Id = SectionId,
                    Name = sectionName,
                    DisplayOrder = 0,
                    IsRequired = true,
                    MinSelection = 1,
                    MaxSelection = 2,
                    Items =
                    [
                        new MenuSectionItemDto { ProductId = ExistingChoiceId, DisplayOrder = 0, IsDefault = true },
                        new MenuSectionItemDto { ProductId = AddedChoiceId, DisplayOrder = 1 }
                    ]
                }
            ]
        },
        Content: new ProductDescriptionsDto());

    private static Product Product(Guid id, string name, decimal price) => new()
    {
        Id = id,
        Name = name,
        BasePrice = price,
        Type = ProductType.MainItem,
        IsActive = true,
        IsAvailable = true,
        Ingredients = [],
        Allergens = [],
        CreatedAt = DateTime.UtcNow,
        CreatedBy = Actor
    };

    private static UpdateProductCommand ProductPutCommand(
        ProductDto product,
        List<ProductCustomizationGroupDto> groups) => new(
            Id: product.Id,
            Name: product.Name,
            Description: product.Description,
            BasePrice: product.BasePrice,
            IsActive: product.IsActive,
            IsAvailable: product.IsAvailable,
            IsSpecial: product.IsSpecial,
            PreparationTimeMinutes: product.PreparationTimeMinutes,
            Type: product.Type,
            KitchenType: product.KitchenType,
            Ingredients: product.Ingredients,
            Allergens: product.Allergens,
            DisplayOrder: product.DisplayOrder,
            CategoryIds: product.Categories.Select(category => category.CategoryId).ToList(),
            PrimaryCategoryId: product.PrimaryCategory?.Id,
            Variations: [],
            SuggestedSideItemIds: [],
            DetailedIngredients: product.DetailedIngredients ?? [],
            MenuDefinition: null,
            Content: product.Content,
            AvailableOrderTypes: product.AvailableOrderTypes,
            HideBaseProduct: product.HideBaseProduct,
            SauceMin: product.SauceMin,
            SauceMax: product.SauceMax,
            SauceIncludedFree: product.SauceIncludedFree,
            IsComponent: product.IsComponent,
            CustomizationGroups: groups);
}
