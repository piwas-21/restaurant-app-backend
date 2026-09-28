using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Menus.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Menus;

[Collection("Database Lane 1")]
public sealed class AttachedBundleChoiceOptionSetGuardTests : IntegrationTestBase
{
    private const string Actor = "attached-option-set-patch-test";
    private static readonly Guid BundleId = Guid.NewGuid();
    private static readonly Guid DefinitionId = Guid.NewGuid();
    private static readonly Guid ManagedSectionId = Guid.NewGuid();
    private static readonly Guid OtherSectionId = Guid.NewGuid();
    private static readonly Guid ManagedItemId = Guid.NewGuid();
    private static readonly Guid OtherItemId = Guid.NewGuid();
    private static readonly Guid SecondOtherItemId = Guid.NewGuid();
    private static readonly Guid ManagedProductId = Guid.NewGuid();
    private static readonly Guid ManagedVariationId = Guid.NewGuid();
    private static readonly Guid AlternateManagedVariationId = Guid.NewGuid();
    private static readonly Guid OtherProductId = Guid.NewGuid();
    private static readonly Guid SecondOtherProductId = Guid.NewGuid();
    private static readonly Guid OptionSetId = Guid.NewGuid();
    private static readonly Guid OptionSetEntryId = Guid.NewGuid();
    private static readonly Guid AttachmentId = Guid.NewGuid();
    private static readonly Guid AppliedRowId = Guid.NewGuid();

    public AttachedBundleChoiceOptionSetGuardTests(DatabaseFixture databaseFixture) : base(databaseFixture)
    {
    }

    [Fact]
    public async Task Translation_changes_on_attached_and_unrelated_sections_preserve_managed_rules_and_rows()
    {
        AuthenticateAsAdmin();
        var response = await PatchSectionsAsync(
        [
            ManagedSection(translations: new Dictionary<string, object>
            {
                ["fr"] = new { name = "Choisir une viande" }
            }),
            OtherSection(translations: new Dictionary<string, object>
            {
                ["fr"] = new { name = "Choisir un accompagnement" }
            })
        ]);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var result = await ReadResponseAsync<ApiResponse<MenuSectionsPatchResultDto>>(response);
        result!.Data!.AuthoringVersion.Should().Be(3);

        await using var context = DatabaseFixture.CreateContext();
        var section = await context.MenuSections.AsNoTracking()
            .Include(item => item.Items)
            .SingleAsync(item => item.Id == ManagedSectionId);
        section.IsRequired.Should().BeTrue();
        section.MinSelection.Should().Be(1);
        section.MaxSelection.Should().Be(1);
        section.Items.Should().ContainSingle(item => item.Id == ManagedItemId
            && item.AdditionalPrice == 0.5m && item.IsDefault);
        (await context.MenuSectionTranslations.CountAsync(item =>
            item.MenuSectionId == ManagedSectionId || item.MenuSectionId == OtherSectionId))
            .Should().Be(2);
        (await context.OptionSetAppliedRows.AsNoTracking()
            .SingleAsync(item => item.Id == AppliedRowId)).MaterializedRowId.Should().Be(ManagedItemId);
    }

    [Fact]
    public async Task Patch_rejects_managed_surcharge_or_default_changes_without_partial_translation_write()
    {
        AuthenticateAsAdmin();
        var changedItem = new
        {
            id = ManagedItemId,
            productId = ManagedProductId,
            productVariationId = ManagedVariationId,
            additionalPrice = 1.25m,
            displayOrder = 0,
            isDefault = false
        };
        var response = await PatchSectionsAsync(
        [
            ManagedSection(items: [changedItem]),
            OtherSection(translations: new Dictionary<string, object>
            {
                ["fr"] = new { name = "Choisir un accompagnement" }
            })
        ]);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict,
            "a materialized option's surcharge and default are owned by the attached set");
        await AssertManagedStateUnchangedAsync();
        await using var context = DatabaseFixture.CreateContext();
        (await context.MenuSectionTranslations.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Patch_keeps_bad_request_for_invalid_product_reference_on_managed_row()
    {
        AuthenticateAsAdmin();
        var changedItem = new
        {
            id = ManagedItemId,
            productId = Guid.NewGuid(),
            additionalPrice = 1.25m,
            displayOrder = 0,
            isDefault = false
        };
        var response = await PatchSectionsAsync(
        [
            ManagedSection(items: [changedItem]),
            OtherSection()
        ]);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "invalid section references retain the menu editor's established validation response");
        await AssertManagedStateUnchangedAsync();
    }

    [Fact]
    public async Task Patch_keeps_bad_request_for_foreign_row_id_before_managed_removal_conflict()
    {
        AuthenticateAsAdmin();
        var foreignItem = new
        {
            id = Guid.NewGuid(),
            productId = OtherProductId,
            additionalPrice = 0m,
            displayOrder = 0,
            isDefault = false
        };
        var response = await PatchSectionsAsync(
        [
            ManagedSection(items: [foreignItem]),
            OtherSection()
        ]);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "a child ID owned by another or no section is malformed before attached-row protection applies");
        await AssertManagedStateUnchangedAsync();
    }

    [Fact]
    public async Task Patch_rejects_managed_product_variation_and_display_order_changes()
    {
        AuthenticateAsAdmin();
        var changedProduct = await PatchSectionsAsync(
        [
            ManagedSection(items: [new
            {
                id = ManagedItemId,
                productId = OtherProductId,
                productVariationId = (Guid?)null,
                additionalPrice = 0.5m,
                displayOrder = 0,
                isDefault = true
            }]),
            OtherSection()
        ]);
        changedProduct.StatusCode.Should().Be(HttpStatusCode.Conflict,
            "changing a mapped row's product changes the option-set materialization");

        var changedVariation = await PatchSectionsAsync(
        [
            ManagedSection(items: [new
            {
                id = ManagedItemId,
                productId = ManagedProductId,
                productVariationId = AlternateManagedVariationId,
                additionalPrice = 0.5m,
                displayOrder = 0,
                isDefault = true
            }]),
            OtherSection()
        ]);
        changedVariation.StatusCode.Should().Be(HttpStatusCode.Conflict,
            "changing a mapped row's variation changes the option-set materialization");

        var changedOrder = await PatchSectionsAsync(
        [
            ManagedSection(items: [new
            {
                id = ManagedItemId,
                productId = ManagedProductId,
                productVariationId = ManagedVariationId,
                additionalPrice = 0.5m,
                displayOrder = 1,
                isDefault = true
            }]),
            OtherSection()
        ]);
        changedOrder.StatusCode.Should().Be(HttpStatusCode.Conflict,
            "changing a mapped row's display order changes the option-set materialization");
        await AssertManagedStateUnchangedAsync();
    }

    [Fact]
    public async Task Patch_rejects_removing_a_managed_option_or_its_attached_section()
    {
        AuthenticateAsAdmin();
        var removeRow = await PatchSectionsAsync(
        [
            ManagedSection(items: []),
            OtherSection()
        ]);
        removeRow.StatusCode.Should().Be(HttpStatusCode.Conflict,
            "the option-set mapping must keep its materialized row");

        var removeSection = await PatchSectionsAsync([OtherSection()]);
        removeSection.StatusCode.Should().Be(HttpStatusCode.Conflict,
            "the option-set attachment targets this stable section ID");
        await AssertManagedStateUnchangedAsync();
    }

    [Fact]
    public async Task Patch_rejects_changes_to_attached_selection_rules()
    {
        AuthenticateAsAdmin();
        var response = await PatchSectionsAsync(
        [
            ManagedSection(isRequired: false, minSelection: 0),
            OtherSection()
        ]);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict,
            "the selection rule is part of the attached option-set configuration");
        await AssertManagedStateUnchangedAsync();
    }

    protected override async Task SeedTestData()
    {
        await base.SeedTestData();
        await using var context = DatabaseFixture.CreateContext();
        var category = await context.Categories.FirstAsync();

        var managedProduct = NewProduct(ManagedProductId, "Managed protein", ProductType.MainItem, category, 8m);
        var otherProduct = NewProduct(OtherProductId, "First side", ProductType.MainItem, category, 4m);
        var secondOtherProduct = NewProduct(SecondOtherProductId, "Second side", ProductType.MainItem, category, 5m);
        var bundle = NewProduct(BundleId, "Option set bundle", ProductType.Menu, category, 12m);
        managedProduct.Variations.Add(NewVariation(ManagedVariationId, managedProduct, "Regular"));
        managedProduct.Variations.Add(NewVariation(AlternateManagedVariationId, managedProduct, "Large"));
        var definition = new MenuDefinition
        {
            Id = DefinitionId,
            ProductId = BundleId,
            Product = bundle,
            AuthoringVersion = 2,
            VersionedSectionEditingStarted = true,
            IsAlwaysAvailable = true,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = Actor
        };
        var managedSection = NewSection(ManagedSectionId, definition, "Choose protein", 0, true, 1, 1);
        var managedItem = NewSectionItem(ManagedItemId, managedSection, managedProduct, 0.5m, 0, true);
        managedItem.ProductVariationId = ManagedVariationId;
        managedSection.Items.Add(managedItem);
        var otherSection = NewSection(OtherSectionId, definition, "Choose a side", 1, false, 0, 2);
        otherSection.Items.Add(NewSectionItem(OtherItemId, otherSection, otherProduct, 0m, 0, false));
        otherSection.Items.Add(NewSectionItem(SecondOtherItemId, otherSection, secondOtherProduct, 0m, 1, false));
        definition.Sections.Add(managedSection);
        definition.Sections.Add(otherSection);
        bundle.MenuDefinition = definition;

        var optionSet = new OptionSet
        {
            Id = OptionSetId,
            Kind = OptionSetKind.BundleChoice,
            Name = "Protein choices",
            NormalizedName = "protein choices",
            SourceLocale = "en",
            Status = OptionSetStatus.Active,
            Version = 1,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = Actor
        };
        var entry = new OptionSetEntry
        {
            Id = OptionSetEntryId,
            OptionSetId = OptionSetId,
            OptionSet = optionSet,
            Name = "Managed protein",
            DisplayOrder = 0,
            ProductId = ManagedProductId,
            IsEnabled = true,
            MaxQuantity = 1,
            AdditionalPrice = 0.5m,
            IsDefault = true,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = Actor
        };
        var attachment = new OptionSetAttachment
        {
            Id = AttachmentId,
            OptionSetId = OptionSetId,
            OptionSet = optionSet,
            Role = OptionSetAttachmentRole.BundleChoice,
            TargetProductId = BundleId,
            TargetMenuSectionId = ManagedSectionId,
            AppliedSetVersion = 1,
            Version = 1,
            MinSelection = 1,
            MaxSelection = 1,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = Actor
        };
        var appliedRow = new OptionSetAppliedRow
        {
            Id = AppliedRowId,
            OptionSetAttachmentId = AttachmentId,
            OptionSetEntryId = OptionSetEntryId,
            RowType = nameof(MenuSectionItem),
            MaterializedRowId = ManagedItemId,
            OwnsMaterializedRow = true,
            LastAppliedValuesJson = "{\"additionalPrice\":0.5,\"displayOrder\":0,\"isDefault\":true}",
            CreatedAt = DateTime.UtcNow,
            CreatedBy = Actor
        };

        context.AddRange(managedProduct, otherProduct, secondOtherProduct, bundle, definition,
            optionSet, entry, attachment, appliedRow);
        await context.SaveChangesAsync();
    }

    private Task<HttpResponseMessage> PatchSectionsAsync(object[] sections)
    {
        Client.DefaultRequestHeaders.Remove("If-Match");
        Client.DefaultRequestHeaders.TryAddWithoutValidation("If-Match", "\"2\"");
        return PatchAsJsonAsync($"/api/Menus/{BundleId}/sections", new { sections });
    }

    private Task<HttpResponseMessage> PatchAsJsonAsync<T>(string uri, T payload) =>
        Client.PatchAsJsonAsync(uri, payload, JsonOptions);

    private async Task AssertManagedStateUnchangedAsync()
    {
        await using var context = DatabaseFixture.CreateContext();
        var definition = await context.MenuDefinitions.AsNoTracking()
            .Include(item => item.Sections)
                .ThenInclude(section => section.Items)
            .SingleAsync(item => item.ProductId == BundleId);
        definition.AuthoringVersion.Should().Be(2);
        definition.Sections.Should().Contain(section => section.Id == ManagedSectionId
            && section.IsRequired && section.MinSelection == 1 && section.MaxSelection == 1);
        definition.Sections.Single(section => section.Id == ManagedSectionId).Items
            .Should().ContainSingle(item => item.Id == ManagedItemId
                && item.ProductVariationId == ManagedVariationId
                && item.AdditionalPrice == 0.5m && item.IsDefault);
        definition.Sections.Should().Contain(section => section.Id == OtherSectionId);
    }

    private object ManagedSection(
        bool isRequired = true,
        int minSelection = 1,
        object[]? items = null,
        Dictionary<string, object>? translations = null) => new
        {
            id = ManagedSectionId,
            name = "Choose protein",
            description = "Choose one protein",
            displayOrder = 0,
            isRequired,
            minSelection,
            maxSelection = 1,
            items,
            translations
        };

    private object OtherSection(Dictionary<string, object>? translations = null) => new
    {
        id = OtherSectionId,
        name = "Choose a side",
        description = "Choose up to two sides",
        displayOrder = 1,
        isRequired = false,
        minSelection = 0,
        maxSelection = 2,
        translations
    };

    private static MenuSection NewSection(
        Guid id,
        MenuDefinition definition,
        string name,
        int order,
        bool required,
        int minSelection,
        int maxSelection) => new()
        {
            Id = id,
            MenuDefinition = definition,
            Name = name,
            DisplayOrder = order,
            IsRequired = required,
            MinSelection = minSelection,
            MaxSelection = maxSelection,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = Actor
        };

    private static MenuSectionItem NewSectionItem(
        Guid id,
        MenuSection section,
        Product product,
        decimal additionalPrice,
        int order,
        bool isDefault) => new()
        {
            Id = id,
            MenuSection = section,
            Product = product,
            AdditionalPrice = additionalPrice,
            DisplayOrder = order,
            IsDefault = isDefault,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = Actor
        };

    private static ProductVariation NewVariation(Guid id, Product product, string name) => new()
    {
        Id = id,
        ProductId = product.Id,
        Product = product,
        Name = name,
        IsActive = true,
        CreatedAt = DateTime.UtcNow,
        CreatedBy = Actor
    };

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
