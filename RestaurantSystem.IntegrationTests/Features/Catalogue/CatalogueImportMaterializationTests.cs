using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Common.TenantFeatures;
using RestaurantSystem.Api.Features.Catalogue.Dtos;
using RestaurantSystem.Api.Features.Catalogue.Services;
using RestaurantSystem.Api.Features.OptionSets.Materialization;
using RestaurantSystem.Api.Features.Products.Dtos;
using RestaurantSystem.Api.Features.TranslationWorkbench.Services;
using RestaurantSystem.Api.Settings;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Catalogue;

[Collection("Database Lane 4")]
public sealed class CatalogueImportMaterializationTests(DatabaseFixture databaseFixture)
    : IntegrationTestBase(databaseFixture)
{
    private const string Actor = "catalogue-import-materialization-test";
    private static readonly JsonSerializerOptions WebOptions = new(JsonSerializerDefaults.Web);

    protected override void ConfigureTestServices(IServiceCollection services) =>
        services.PostConfigure<TenantFeatureSettings>(settings => settings.OptionSetMaterializationEnabled = true);

    [Fact]
    public async Task Item_import_creates_option_sets_and_materializes_all_roles_using_inactive_staged_rows()
    {
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var category = await context.Categories.AsNoTracking().FirstAsync();
        var choices = await MakeInactiveChoicesAsync(context);
        var ingredients = await MakeIngredientsAsync(context);
        var choiceTemplates = choices.Select((product, index) => ImportedTemplate(
            Revision($"choice-product-{index + 1}", "item", EmptyItemPayload()), "Product", product.Id)).ToArray();
        var ingredientTemplates = ingredients.Select((ingredient, index) =>
        {
            var isSauce = index > 1;
            return ImportedTemplate(Revision(isSauce ? $"sauce-{index - 1}" : $"ingredient-{index + 1}",
                "ingredient", new { suggestedOnly = true, role = isSauce ? "sauce" : "ingredient" }),
                "GlobalIngredient", ingredient.Id);
        }).ToArray();
        var categoryTemplate = ImportedTemplate(
            Revision("source-category", "category", new { sortOrder = 0 }), "Category", category.Id);

        var choiceSet = OptionSetRevision("product-choice-set", "bundle-option", 1, 2,
            ["choice-product-1", "choice-product-2"]);
        var ingredientSet = OptionSetRevision("ingredient-set", "ingredient", 0, 2,
            ["ingredient-1", "ingredient-2"]);
        var sauceSet = OptionSetRevision("sauce-set", "sauce", 0, 2, ["sauce-1", "sauce-2"]);
        var sideSet = OptionSetRevision("side-set", "suggested-side", 0, 2,
            ["choice-product-1", "choice-product-2"]) with
        { Translations = [] };
        var sets = new[]
        {
            PendingTemplate(choiceSet, SetDecision(("choice-product-1@1", 0m), ("choice-product-2@1", 1.25m))),
            PendingTemplate(ingredientSet, SetDecision(("ingredient-1@1", 0.75m), ("ingredient-2@1", 0m))),
            PendingTemplate(sauceSet, SetDecision(("sauce-1@1", 0.5m), ("sauce-2@1", 1m))),
            PendingTemplate(sideSet, SetDecision())
        };
        var itemRevision = Revision("imported-item", "item", new
        {
            category = Reference("source-category"),
            suggestedIngredients = Array.Empty<object>(),
            optionSets = new[]
            {
                Reference("product-choice-set"), Reference("ingredient-set"), Reference("sauce-set")
            },
            sideSets = new[] { Reference("side-set") }
        }, dependencies:
        [
            Dependency("source-category", "category", 0),
            Dependency("product-choice-set", "option-set", 1),
            Dependency("ingredient-set", "option-set", 2),
            Dependency("sauce-set", "option-set", 3),
            Dependency("side-set", "option-set", 4)
        ]);
        var item = PendingTemplate(itemRevision, ReviewedSellableDecision(), isRoot: true);
        var session = NewSession(itemRevision.TemplateId,
            [.. choiceTemplates, .. ingredientTemplates, categoryTemplate, .. sets, item]);
        context.CatalogueImportSessions.Add(session);
        await context.SaveChangesAsync();

        var result = await ImportAsync(scope.ServiceProvider, context, session, [
            .. choiceTemplates.Select(ReadRevision), .. ingredientTemplates.Select(ReadRevision),
            ReadRevision(categoryTemplate), choiceSet, ingredientSet, sauceSet, sideSet, itemRevision
        ]);

        result.Status.Should().Be(nameof(CatalogueImportStatus.Imported));
        result.Items.Single(itemResult => itemResult.TemplateId == "imported-item").LocalEntityType.Should().Be("Product");
        var productId = result.Items.Single(itemResult => itemResult.TemplateId == "imported-item").LocalEntityId;
        productId.Should().NotBeNull();
        var product = await context.Products.AsNoTracking()
            .Include(value => value.CustomizationGroups).ThenInclude(group => group.ProductOptions)
            .Include(value => value.DetailedIngredients)
            .Include(value => value.SuggestedSideItems)
            .SingleAsync(value => value.Id == productId);
        product.IsActive.Should().BeFalse();
        product.IsAvailable.Should().BeFalse();
        product.CustomizationGroups.Should().ContainSingle();
        product.CustomizationGroups.Single().AuthoringVersion.Should().Be(2);
        product.CustomizationGroups.Single().MinSelection.Should().Be(1);
        product.CustomizationGroups.Single().MaxSelection.Should().Be(2);
        product.CustomizationGroups.Single().ProductOptions.Should().HaveCount(2)
            .And.OnlyContain(option => choices.Select(choice => choice.Id).Contains(option.OptionProductId));
        product.DetailedIngredients.Should().HaveCount(4);
        product.SauceMin.Should().Be(0);
        product.SauceMax.Should().Be(2);
        product.SuggestedSideItems.Should().HaveCount(2);

        var attachments = await context.OptionSetAttachments.AsNoTracking().ToListAsync();
        attachments.Select(attachment => attachment.Role).Should().BeEquivalentTo(
        [
            OptionSetAttachmentRole.ProductChoice, OptionSetAttachmentRole.Ingredient,
            OptionSetAttachmentRole.Sauce, OptionSetAttachmentRole.SuggestedSide
        ]);
        var optionSets = await context.OptionSets.AsNoTracking().Include(set => set.Entries)
            .Include(set => set.Translations).ToListAsync();
        optionSets.Should().HaveCount(4);
        optionSets.Should().OnlyContain(set => set.SourceTemplateId != null && set.SourceRevision == 1);
        optionSets.Single(set => set.SourceTemplateId == "side-set").Translations.Should().BeEmpty();
        optionSets.Single(set => set.SourceTemplateId == "product-choice-set").Entries
            .Should().ContainSingle(entry => entry.ProductId == choices[1].Id && entry.AdditionalPrice == 1.25m);
        var setProvenance = await context.TranslationFieldProvenances.AsNoTracking()
            .Where(row => row.EntityType == "optionSet" && optionSets.Select(set => set.Id).Contains(row.EntityId))
            .ToListAsync();
        setProvenance.Should().Contain(row => row.Locale == "en" && row.Kind == "template");
        setProvenance.Should().Contain(row => row.Locale == "fr" && row.Kind == "template");
        var itemProvenance = await context.TranslationFieldProvenances.AsNoTracking()
            .Where(row => row.EntityType == "product" && row.EntityId == product.Id && row.FieldKey == "name")
            .ToListAsync();
        itemProvenance.Should().Contain(row => row.Locale == "en" && row.Kind == "template" &&
            row.TemplateId == "imported-item");
        itemProvenance.Should().Contain(row => row.Locale == "fr" && row.Kind == "template" &&
            row.TemplateId == "imported-item");

        var replay = await ImportAsync(scope.ServiceProvider, context, session, [
            .. choiceTemplates.Select(ReadRevision), .. ingredientTemplates.Select(ReadRevision),
            ReadRevision(categoryTemplate), choiceSet, ingredientSet, sauceSet, sideSet, itemRevision
        ]);
        replay.Items.Should().BeEquivalentTo(result.Items);
        (await context.OptionSets.CountAsync()).Should().Be(4);
    }

    [Fact]
    public async Task Bundle_import_persists_stable_sections_and_applies_choice_rows_with_template_provenance()
    {
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var choices = await MakeInactiveChoicesAsync(context);
        var choiceTemplates = choices.Select((product, index) => ImportedTemplate(
            Revision($"bundle-option-{index + 1}", "item", EmptyItemPayload()), "Product", product.Id)).ToArray();
        var section = new
        {
            sectionKey = "main-choice",
            name = "Choose a main",
            sortOrder = 0,
            min = 1,
            max = 2,
            translations = new { fr = new { name = "Choisissez un plat" } },
            options = new[]
            {
                new { templateId = "bundle-option-1", revision = 1, sortOrder = 0, @default = true },
                new { templateId = "bundle-option-2", revision = 1, sortOrder = 1, @default = false }
            }
        };
        var bundleRevision = Revision("imported-bundle", "bundle", new { sections = new[] { section } },
            description: "A reviewed bundle description.", dependencies:
            [Dependency("bundle-option-1", "item", 0), Dependency("bundle-option-2", "item", 1)]);
        var decision = ReviewedSellableDecision() with
        {
            LocalPrice = 14.25m,
            ChoiceRulesReviewed = true,
            OptionPricesReviewed = true,
            LocalOptionPrices = new Dictionary<string, decimal>
            {
                ["bundle-option-1@1"] = 0m,
                ["bundle-option-2@1"] = 2m
            }
        };
        var bundle = PendingTemplate(bundleRevision, decision, isRoot: true);
        var session = NewSession(bundleRevision.TemplateId, [.. choiceTemplates, bundle]);
        context.CatalogueImportSessions.Add(session);
        await context.SaveChangesAsync();

        var result = await ImportAsync(scope.ServiceProvider, context, session,
            [.. choiceTemplates.Select(ReadRevision), bundleRevision]);

        result.Status.Should().Be(nameof(CatalogueImportStatus.Imported));
        var bundleId = result.Items.Single(item => item.TemplateId == "imported-bundle").LocalEntityId;
        bundleId.Should().NotBeNull();
        var product = await context.Products.AsNoTracking()
            .Include(value => value.MenuDefinition).ThenInclude(definition => definition!.Sections)
            .ThenInclude(sectionValue => sectionValue.Items)
            .SingleAsync(value => value.Id == bundleId);
        product.Type.Should().Be(ProductType.Menu);
        product.IsActive.Should().BeFalse();
        product.IsAvailable.Should().BeFalse();
        product.MenuDefinition.Should().NotBeNull();
        product.MenuDefinition!.AuthoringVersion.Should().Be(2);
        product.MenuDefinition.VersionedSectionEditingStarted.Should().BeTrue();
        product.MenuDefinition.Sections.Should().ContainSingle();
        var savedSection = product.MenuDefinition.Sections.Single();
        savedSection.Id.Should().NotBe(Guid.Empty);
        savedSection.Name.Should().Be("Choose a main");
        savedSection.Items.Should().HaveCount(2);
        savedSection.Items.Should().ContainSingle(row => row.ProductId == choices[0].Id && row.IsDefault);
        savedSection.Items.Should().ContainSingle(row => row.ProductId == choices[1].Id && row.AdditionalPrice == 2m);

        var set = await context.OptionSets.AsNoTracking().Include(value => value.Entries)
            .SingleAsync(value => value.SourceTemplateId == "imported-bundle" && value.SourceOptionSetId == "main-choice");
        set.Kind.Should().Be(OptionSetKind.BundleChoice);
        set.Entries.Should().HaveCount(2);
        var attachment = await context.OptionSetAttachments.AsNoTracking()
            .SingleAsync(value => value.OptionSetId == set.Id);
        attachment.Role.Should().Be(OptionSetAttachmentRole.BundleChoice);
        attachment.TargetProductId.Should().Be(product.Id);
        attachment.TargetMenuSectionId.Should().Be(savedSection.Id);
        attachment.MinSelection.Should().Be(1);
        attachment.MaxSelection.Should().Be(2);

        var textRows = await context.TranslationFieldProvenances.AsNoTracking()
            .Where(row => (row.EntityType == "product" && row.EntityId == product.Id) ||
                (row.EntityType == "menuSection" && row.EntityId == savedSection.Id) ||
                (row.EntityType == "optionSet" && row.EntityId == set.Id))
            .ToListAsync();
        textRows.Should().Contain(row => row.EntityType == "product" && row.Locale == "fr" &&
            row.Kind == "template" && row.TemplateId == "imported-bundle");
        textRows.Should().Contain(row => row.EntityType == "menuSection" && row.Locale == "fr" &&
            row.Kind == "template" && row.TemplateId == "imported-bundle");
        textRows.Should().Contain(row => row.EntityType == "optionSet" && row.Locale == "en" &&
            row.Kind == "template" && row.TemplateId == "imported-bundle");
    }

    [Fact]
    public async Task Item_import_rolls_back_its_new_product_when_a_reused_option_set_has_the_wrong_kind()
    {
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var category = await context.Categories.AsNoTracking().FirstAsync();
        var choices = await MakeInactiveChoicesAsync(context);
        var sourceChoices = choices.Select((product, index) => ImportedTemplate(
            Revision($"rollback-choice-{index + 1}", "item", EmptyItemPayload()), "Product", product.Id)).ToArray();
        var categoryTemplate = ImportedTemplate(
            Revision("rollback-category", "category", new { sortOrder = 0 }), "Category", category.Id);
        var optionSetRevision = OptionSetRevision("rollback-set", "bundle-option", 1, 2,
            ["rollback-choice-1", "rollback-choice-2"]);
        var wrongKindName = $"Wrong kind {Guid.NewGuid():N}";
        var wrongKindSet = new OptionSet
        {
            Id = Guid.NewGuid(),
            Kind = OptionSetKind.Sauce,
            Name = wrongKindName,
            NormalizedName = wrongKindName.ToLowerInvariant(),
            SourceLocale = "en",
            Status = OptionSetStatus.Active,
            Version = 1,
            CreatedBy = Actor
        };
        context.OptionSets.Add(wrongKindSet);
        var optionSetTemplate = ImportedTemplate(optionSetRevision, "OptionSet", wrongKindSet.Id);
        var itemRevision = Revision("rollback-item", "item", new
        {
            category = Reference("rollback-category"),
            suggestedIngredients = Array.Empty<object>(),
            optionSets = new[] { Reference("rollback-set") },
            sideSets = Array.Empty<object>()
        }, dependencies:
        [
            Dependency("rollback-category", "category", 0),
            Dependency("rollback-set", "option-set", 1)
        ]);
        var item = PendingTemplate(itemRevision, ReviewedSellableDecision(), isRoot: true);
        var session = NewSession(itemRevision.TemplateId,
            [.. sourceChoices, categoryTemplate, optionSetTemplate, item]);
        context.CatalogueImportSessions.Add(session);
        await context.SaveChangesAsync();
        var productCountBefore = await context.Products.CountAsync();

        var result = await ImportAsync(scope.ServiceProvider, context, session,
            [.. sourceChoices.Select(ReadRevision), ReadRevision(categoryTemplate), optionSetRevision, itemRevision]);

        result.Status.Should().Be(nameof(CatalogueImportStatus.PartiallyImported));
        result.Items.Single(resultItem => resultItem.TemplateId == "rollback-item").Status
            .Should().Be(nameof(CatalogueImportItemStatus.Failed));
        result.Items.Single(resultItem => resultItem.TemplateId == "rollback-item").FailureCode
            .Should().Be("IMPORT_VALIDATION_FAILED");
        (await context.Products.CountAsync()).Should().Be(productCountBefore);
        (await context.Products.AnyAsync(product => product.Name == "rollback-item français")).Should().BeFalse();
        (await context.CatalogueImportSessionTemplates.AsNoTracking()
            .SingleAsync(template => template.TemplateId == "rollback-item")).FailureCode
            .Should().Be("IMPORT_VALIDATION_FAILED");
    }

    private static async Task<CatalogueImportResultDto> ImportAsync(
        IServiceProvider services,
        ApplicationDbContext context,
        CatalogueImportSession session,
        IReadOnlyList<CentralCatalogueTemplateRevision> revisions)
    {
        var byId = revisions.ToDictionary(revision => revision.TemplateId, StringComparer.Ordinal);
        var catalogue = new Mock<ICentralCatalogueClient>(MockBehavior.Strict);
        catalogue.Setup(client => client.GetCurrentRevisionBatchAsync(
                It.IsAny<IReadOnlyList<CatalogueCurrentRevisionRequest>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<CatalogueCurrentRevisionRequest> requests, CancellationToken _) =>
                new CatalogueProxyResponse(StatusCodes.Status200OK, JsonSerializer.SerializeToElement(new
                {
                    items = requests.Select(request => new
                    {
                        templateId = request.TemplateId,
                        status = "available",
                        revision = byId[request.TemplateId],
                        adoptedRevisionWithdrawn = false
                    })
                }, WebOptions)));
        var importLock = new Mock<ICatalogueImportLock>();
        importLock.Setup(value => value.AcquireAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NoopLease());
        var currentUser = services.GetRequiredService<ICurrentUserService>();
        var importer = new CatalogueSessionImporter(
            context,
            importLock.Object,
            new CatalogueImportStateStore(context, currentUser),
            services.GetRequiredService<ICatalogueTemplateImportExecutor>(),
            services.GetRequiredService<ICatalogueImportPreviewService>(),
            catalogue.Object,
            services.GetRequiredService<ILogger<CatalogueSessionImporter>>());

        var preview = await services.GetRequiredService<ICatalogueImportPreviewService>()
            .PreviewAsync(session.Id, CancellationToken.None);
        preview.Items.Where(item => item.IsSelected).SelectMany(item => item.BlockingIssues)
            .Should().BeEmpty();

        return await importer.ImportAsync(session.Id,
            new ImportCatalogueSessionRequest { ExpectedVersion = 1, IdempotencyKey = Guid.NewGuid().ToString("N") },
            CancellationToken.None);
    }

    private static CatalogueImportSession NewSession(
        string rootTemplateId,
        IReadOnlyCollection<CatalogueImportSessionTemplate> templates)
    {
        var session = new CatalogueImportSession
        {
            Id = Guid.NewGuid(),
            RootTemplateId = rootTemplateId,
            RootRevision = 1,
            Locale = "fr",
            IdempotencyKey = Guid.NewGuid().ToString("N"),
            AdoptionId = Guid.NewGuid(),
            CreateNewCopy = true,
            Version = 1,
            CreatedBy = Actor,
            Templates = templates.ToList()
        };
        return session;
    }

    private static CatalogueImportSessionTemplate ImportedTemplate(
        CentralCatalogueTemplateRevision revision,
        string localEntityType,
        Guid localEntityId) => Template(revision, CatalogueImportItemStatus.Imported,
        localEntityType, localEntityId, null, isRoot: false);

    private static CatalogueImportSessionTemplate PendingTemplate(
        CentralCatalogueTemplateRevision revision,
        CatalogueImportItemDecision decision,
        bool isRoot = false) => Template(revision,
        CatalogueImportItemStatus.Pending, null, null, decision, isRoot);

    private static CatalogueImportSessionTemplate Template(
        CentralCatalogueTemplateRevision revision,
        CatalogueImportItemStatus status,
        string? localEntityType,
        Guid? localEntityId,
        CatalogueImportItemDecision? decision,
        bool isRoot) => new()
        {
            Id = Guid.NewGuid(),
            TemplateId = revision.TemplateId,
            Revision = revision.Revision,
            Type = revision.Type,
            ContentHash = revision.ContentHash,
            RevisionJson = JsonSerializer.Serialize(revision, WebOptions),
            DecisionJson = decision is null ? null : JsonSerializer.Serialize(decision, WebOptions),
            IsRoot = isRoot,
            IsSelectable = true,
            IsSelected = true,
            Status = status,
            LocalEntityType = localEntityType,
            LocalEntityId = localEntityId,
            CreatedBy = Actor
        };

    private static CentralCatalogueTemplateRevision ReadRevision(CatalogueImportSessionTemplate template) =>
        JsonSerializer.Deserialize<CentralCatalogueTemplateRevision>(template.RevisionJson, WebOptions)!;

    private static CentralCatalogueTemplateRevision Revision(
        string templateId,
        string type,
        object payload,
        string? description = null,
        IReadOnlyList<CentralCatalogueDependency>? dependencies = null) => new()
        {
            SchemaVersion = 1,
            TemplateId = templateId,
            Revision = 1,
            Type = type,
            Name = $"Reviewed {templateId}",
            Description = description,
            SourceLocale = "en",
            Translations = new Dictionary<string, CentralCatalogueTranslation>(StringComparer.OrdinalIgnoreCase)
            {
                ["fr"] = new() { Name = $"{templateId} français", Description = description is null ? null : $"{description} français" }
            },
            Dependencies = dependencies?.ToList() ?? [],
            Provenance = JsonSerializer.SerializeToElement(new { source = "fixture" }),
            QualityStatus = "reviewed",
            CompatibleTenantContractVersions = [1],
            Payload = JsonSerializer.SerializeToElement(payload),
            ContentHash = new string('a', 64)
        };

    private static CentralCatalogueTemplateRevision OptionSetRevision(
        string templateId,
        string kind,
        int minimum,
        int maximum,
        IReadOnlyList<string> references)
    {
        var options = references.Select((reference, index) => new
        {
            templateId = reference,
            revision = 1,
            sortOrder = index,
            @default = index == 0 && kind == "bundle-option"
        }).ToArray();
        return Revision(templateId, "option-set", new { kind, min = minimum, max = maximum, options },
            dependencies: references.Select((reference, index) => Dependency(reference,
                kind is "ingredient" or "sauce" ? "ingredient" : "item", index)).ToArray());
    }

    private static CentralCatalogueDependency Dependency(string templateId, string role, int order) => new()
    {
        TemplateId = templateId,
        Revision = 1,
        Role = role,
        SortOrder = order
    };

    private static object Reference(string templateId) => new { templateId, revision = 1 };

    private static object EmptyItemPayload() => new
    {
        category = (object?)null,
        suggestedIngredients = Array.Empty<object>(),
        optionSets = Array.Empty<object>(),
        sideSets = Array.Empty<object>()
    };

    private static CatalogueImportItemDecision SetDecision(params (string Reference, decimal Price)[] prices) => new()
    {
        Resolution = "Create",
        ChoiceRulesReviewed = true,
        OptionPricesReviewed = true,
        LocalOptionPrices = prices.ToDictionary(price => price.Reference, price => price.Price, StringComparer.Ordinal)
    };

    private static CatalogueImportItemDecision ReviewedSellableDecision() => new()
    {
        Resolution = "Create",
        LocalPrice = 8.75m,
        LocalProductType = "MainItem",
        IntendedIsAvailable = false,
        KitchenType = KitchenType.BackKitchen,
        Ingredients = ["Tenant reviewed ingredient"],
        Allergens = [],
        IngredientsReviewed = true,
        AllergensReviewed = true,
        AvailabilityReviewed = true,
        ChannelsReviewed = true,
        KitchenRoutingReviewed = true,
        ChoiceRulesReviewed = true
    };

    private static async Task<List<Product>> MakeInactiveChoicesAsync(ApplicationDbContext context)
    {
        var choices = await context.Products.OrderBy(product => product.Name).Take(2).ToListAsync();
        choices.Should().HaveCount(2);
        foreach (var product in choices)
        {
            product.IsActive = false;
            product.IsAvailable = false;
        }

        await context.SaveChangesAsync();
        return choices;
    }

    private static async Task<List<GlobalIngredient>> MakeIngredientsAsync(ApplicationDbContext context)
    {
        var suffix = Guid.NewGuid().ToString("N");
        var ingredients = new List<GlobalIngredient>
        {
            new() { DefaultName = $"Import ingredient one {suffix}", IsActive = true, Kind = IngredientKind.Ingredient,
                Origin = LibraryOrigin.Custom, CreatedBy = Actor },
            new() { DefaultName = $"Import ingredient two {suffix}", IsActive = true, Kind = IngredientKind.Ingredient,
                Origin = LibraryOrigin.Custom, CreatedBy = Actor },
            new() { DefaultName = $"Import sauce one {suffix}", IsActive = true, Kind = IngredientKind.Sauce,
                Origin = LibraryOrigin.Custom, CreatedBy = Actor },
            new() { DefaultName = $"Import sauce two {suffix}", IsActive = true, Kind = IngredientKind.Sauce,
                Origin = LibraryOrigin.Custom, CreatedBy = Actor }
        };
        context.GlobalIngredients.AddRange(ingredients);
        await context.SaveChangesAsync();
        return ingredients;
    }

    private sealed class NoopLease : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
