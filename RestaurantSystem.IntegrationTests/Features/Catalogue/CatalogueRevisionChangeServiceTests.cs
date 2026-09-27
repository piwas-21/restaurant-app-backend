using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Catalogue.Dtos;
using RestaurantSystem.Api.Features.Catalogue.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Catalogue;

[Collection("Database Lane 3")]
public sealed class CatalogueRevisionChangeServiceTests(DatabaseFixture databaseFixture)
    : IntegrationTestBase(databaseFixture)
{
    [Fact]
    public async Task Get_reports_withdrawn_sources_and_deleted_local_records_without_applying_changes()
    {
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var currentUser = scope.ServiceProvider.GetRequiredService<ICurrentUserService>();
        var sessionId = Guid.NewGuid();
        var adoptionId = Guid.NewGuid();
        var deletedTemplate = "deleted-local-template";
        var withdrawnTemplate = "withdrawn-template";
        var deletedCategory = new Category
        {
            Id = Guid.NewGuid(),
            Name = "Soft-deleted category",
            IsDeleted = true,
            CreatedBy = "test"
        };
        var visibleCategory = new Category
        {
            Id = Guid.NewGuid(),
            Name = "Visible category",
            CreatedBy = "test"
        };
        var session = new CatalogueImportSession
        {
            Id = sessionId,
            RootTemplateId = deletedTemplate,
            RootRevision = 1,
            Locale = "en",
            IdempotencyKey = $"revision-get-test-{sessionId:N}",
            AdoptionId = adoptionId,
            CreatedBy = "test"
        };
        var deletedRevision = CategoryRevision(deletedTemplate, 1);
        var withdrawnRevision = CategoryRevision(withdrawnTemplate, 1);
        context.Categories.AddRange(deletedCategory, visibleCategory);
        context.CatalogueImportSessions.Add(session);
        context.CatalogueTemplateAdoptions.AddRange(
            Adoption(session, deletedTemplate, deletedRevision, deletedCategory.Id),
            Adoption(session, withdrawnTemplate, withdrawnRevision, visibleCategory.Id));
        await context.SaveChangesAsync();

        var catalogue = new Mock<ICentralCatalogueClient>();
        catalogue.Setup(value => value.GetCurrentRevisionBatchAsync(
                It.IsAny<IReadOnlyList<CatalogueCurrentRevisionRequest>>(), It.IsAny<CancellationToken>()))
            .Returns((IReadOnlyList<CatalogueCurrentRevisionRequest> requests, CancellationToken _) =>
            {
                var items = requests.Select(request =>
                {
                    var isWithdrawn = request.TemplateId == withdrawnTemplate;
                    var revision = isWithdrawn
                        ? JsonDocument.Parse("null").RootElement.Clone()
                        : JsonSerializer.SerializeToElement(CategoryRevision(
                            request.TemplateId, request.AdoptedRevision + 1),
                            new JsonSerializerOptions(JsonSerializerDefaults.Web));
                    return new
                    {
                        templateId = request.TemplateId,
                        status = isWithdrawn ? "withdrawn" : "available",
                        revision,
                        adoptedRevisionWithdrawn = (bool?)(isWithdrawn ? true : false)
                    };
                }).ToArray();
                return Task.FromResult(Response(StatusCodes.Status200OK, new { items }));
            });

        var service = new CatalogueRevisionChangeService(context, catalogue.Object, currentUser);
        var result = await service.GetAsync(sessionId, CancellationToken.None);

        result.Items.Single(item => item.TemplateId == deletedTemplate).Status.Should().Be("LocalRecordMissing");
        result.Items.Single(item => item.TemplateId == deletedTemplate).LocalHash.Should().BeNull();
        var withdrawn = result.Items.Single(item => item.TemplateId == withdrawnTemplate);
        withdrawn.Status.Should().Be("AdoptedRevisionWithdrawn");
        withdrawn.Withdrawn.Should().BeTrue();
        withdrawn.Fields.Should().BeEmpty();
        catalogue.Verify(value => value.GetCurrentRevisionBatchAsync(
            It.Is<IReadOnlyList<CatalogueCurrentRevisionRequest>>(items => items.Count == 2),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Applying_bundle_text_across_revisions_copies_only_the_latest_section_mapping()
    {
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var currentUser = scope.ServiceProvider.GetRequiredService<ICurrentUserService>();
        var sessionId = Guid.NewGuid();
        var adoptionId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var definitionId = Guid.NewGuid();
        var sectionId = Guid.NewGuid();
        var revisionOne = BundleRevision(1);
        var revisionTwo = BundleRevision(2);
        var revisionThree = BundleRevision(3);
        var product = new Product
        {
            Id = productId,
            Name = "Tenant bundle",
            BasePrice = 10m,
            Type = ProductType.Menu,
            KitchenType = KitchenType.None,
            Allergens = ["Tenant allergen"],
            IsActive = false,
            IsAvailable = false,
            CreatedBy = "test"
        };
        var definition = new MenuDefinition
        {
            Id = definitionId,
            ProductId = productId,
            Product = product,
            AuthoringVersion = 1,
            CreatedBy = "test"
        };
        var section = new MenuSection
        {
            Id = sectionId,
            MenuDefinitionId = definitionId,
            MenuDefinition = definition,
            Name = "Drinks 1",
            MinSelection = 0,
            MaxSelection = 1,
            CreatedBy = "test",
            Translations =
            [
                new MenuSectionTranslation
                {
                    Id = Guid.NewGuid(),
                    LanguageCode = "tr",
                    Name = "İçecekler 1",
                    CreatedBy = "test"
                }
            ]
        };
        section.Translations.Single().MenuSection = section;
        definition.Sections.Add(section);
        product.MenuDefinition = definition;
        var session = new CatalogueImportSession
        {
            Id = sessionId,
            RootTemplateId = revisionOne.TemplateId,
            RootRevision = revisionOne.Revision,
            Locale = "tr",
            IdempotencyKey = $"revision-bundle-test-{sessionId:N}",
            AdoptionId = adoptionId,
            Version = 1,
            CreatedBy = "test"
        };
        var root = new CatalogueTemplateAdoption
        {
            Id = Guid.NewGuid(),
            AdoptionId = adoptionId,
            SessionId = sessionId,
            SourceTemplateId = revisionOne.TemplateId,
            SourceRevision = revisionOne.Revision,
            LocalEntityType = "MenuBundle",
            LocalEntityId = productId,
            ContentHash = revisionOne.ContentHash,
            BaselineFieldsJson = CatalogueRevisionBaseline.Create(revisionOne, "bundle"),
            CreatedBy = "test"
        };
        var sectionMapping = new CatalogueTemplateAdoption
        {
            Id = Guid.NewGuid(),
            AdoptionId = adoptionId,
            SessionId = sessionId,
            SourceTemplateId = revisionOne.TemplateId,
            SourceRevision = revisionOne.Revision,
            SourceEntryId = "drinks",
            LocalEntityType = "MenuSection",
            LocalEntityId = sectionId,
            ContentHash = revisionOne.ContentHash,
            CreatedBy = "test"
        };
        context.Products.Add(product);
        context.MenuDefinitions.Add(definition);
        context.CatalogueImportSessions.Add(session);
        context.CatalogueTemplateAdoptions.AddRange(root, sectionMapping);
        await context.SaveChangesAsync();

        var catalogue = new Mock<ICentralCatalogueClient>();
        catalogue.Setup(value => value.GetCurrentRevisionBatchAsync(
                It.IsAny<IReadOnlyList<CatalogueCurrentRevisionRequest>>(), It.IsAny<CancellationToken>()))
            .Returns((IReadOnlyList<CatalogueCurrentRevisionRequest> requests, CancellationToken _) =>
            {
                var items = requests.Select(request => new
                {
                    templateId = request.TemplateId,
                    status = "available",
                    revision = request.AdoptedRevision == 1 ? revisionTwo : revisionThree,
                    adoptedRevisionWithdrawn = false
                }).ToArray();
                return Task.FromResult(Response(StatusCodes.Status200OK, new { items }));
            });
        var service = new CatalogueRevisionChangeService(context, catalogue.Object, currentUser);

        var firstPreview = await service.GetAsync(sessionId, CancellationToken.None);
        var firstApply = await service.ApplyFieldsAsync(sessionId, ApplyRequest(
            firstPreview, revisionOne, revisionTwo, ["sections[drinks].name"]), CancellationToken.None);
        var secondPreview = await service.GetAsync(sessionId, CancellationToken.None);
        var secondApply = await service.ApplyFieldsAsync(sessionId, ApplyRequest(
            secondPreview, revisionTwo, revisionThree,
            ["sections[drinks].translations[tr].name"]), CancellationToken.None);

        firstApply.SessionVersion.Should().Be(2);
        secondApply.SessionVersion.Should().Be(3);
        var updated = await context.MenuSections.AsNoTracking().Include(value => value.Translations)
            .SingleAsync(value => value.Id == sectionId);
        updated.Name.Should().Be("Drinks 2");
        updated.Translations.Single().Name.Should().Be("İçecekler 3");
        var newestEntries = await context.CatalogueTemplateAdoptions.AsNoTracking()
            .Where(value => value.AdoptionId == adoptionId && value.SourceTemplateId == revisionOne.TemplateId &&
                value.SourceRevision == revisionThree.Revision && value.SourceEntryId != null)
            .ToListAsync();
        newestEntries.Should().ContainSingle().Which.LocalEntityId.Should().Be(sectionId);
        var updatedProduct = await context.Products.AsNoTracking().SingleAsync(value => value.Id == productId);
        updatedProduct.BasePrice.Should().Be(10m);
        updatedProduct.Allergens.Should().ContainSingle().Which.Should().Be("Tenant allergen");
        updatedProduct.IsActive.Should().BeFalse();
        updatedProduct.IsAvailable.Should().BeFalse();
    }

    [Fact]
    public async Task Applying_one_text_field_preserves_other_tenant_fields_and_rejects_a_stale_local_hash()
    {
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var currentUser = scope.ServiceProvider.GetRequiredService<ICurrentUserService>();
        var central = new Mock<ICentralCatalogueClient>();
        var revisionOne = Revision(1, "Central name 1", "Central description 1", "Central TR name 1");
        var revisionTwo = Revision(2, "Central name 2", "Central description 2", "Central TR name 2");
        central.Setup(value => value.GetCurrentRevisionBatchAsync(
                It.Is<IReadOnlyList<CatalogueCurrentRevisionRequest>>(items =>
                    items.Count == 1 && items[0].TemplateId == "revision-item"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response(StatusCodes.Status200OK, new
            {
                items = new[]
                {
                    new
                    {
                        templateId = "revision-item",
                        status = "available",
                        revision = revisionTwo,
                        adoptedRevisionWithdrawn = false
                    }
                }
            }));

        var sessionId = Guid.NewGuid();
        var adoptionId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var localName = "Tenant edited name";
        var localDescription = "Tenant edited description";
        var localTranslationName = "Lokalde düzenlenen ad";
        var localTranslationDescription = "Lokalde düzenlenen açıklama";
        var session = new CatalogueImportSession
        {
            Id = sessionId,
            RootTemplateId = "revision-item",
            RootRevision = 1,
            Locale = "tr",
            IdempotencyKey = $"revision-test-{sessionId:N}",
            AdoptionId = adoptionId,
            CreateNewCopy = false,
            Version = 1,
            CreatedBy = "test"
        };
        var product = new Product
        {
            Id = productId,
            Name = localName,
            Description = localDescription,
            BasePrice = 8.75m,
            Type = ProductType.MainItem,
            KitchenType = KitchenType.BackKitchen,
            Ingredients = ["Tenant ingredient"],
            Allergens = ["Tenant allergen"],
            IsActive = false,
            IsAvailable = false,
            CreatedBy = "test"
        };
        product.Descriptions.Add(new ProductDescription
        {
            Id = Guid.NewGuid(),
            ProductId = productId,
            Lang = "tr",
            Name = localTranslationName,
            Description = localTranslationDescription,
            CreatedBy = "test"
        });
        var adoption = new CatalogueTemplateAdoption
        {
            Id = Guid.NewGuid(),
            AdoptionId = adoptionId,
            SessionId = sessionId,
            SourceTemplateId = "revision-item",
            SourceRevision = 1,
            LocalEntityType = "Product",
            LocalEntityId = productId,
            ContentHash = revisionOne.ContentHash,
            BaselineFieldsJson = CatalogueRevisionBaseline.Create(revisionOne, "item"),
            IsDefault = true,
            CreatedBy = "test"
        };
        context.CatalogueImportSessions.Add(session);
        context.Products.Add(product);
        context.CatalogueTemplateAdoptions.Add(adoption);
        await context.SaveChangesAsync();

        var service = new CatalogueRevisionChangeService(context, central.Object, currentUser);
        var expectedLocalHash = CatalogueRevisionBaseline.ComputeLocalHash(new Dictionary<string, string?>
        {
            ["name"] = localName,
            ["description"] = localDescription,
            ["translations[tr].name"] = localTranslationName,
            ["translations[tr].description"] = localTranslationDescription
        });
        var request = new ApplyCatalogueRevisionFieldsRequest
        {
            ExpectedSessionVersion = 1,
            TemplateId = "revision-item",
            AdoptedRevision = 1,
            CurrentRevision = 2,
            CurrentContentHash = revisionTwo.ContentHash,
            ExpectedLocalHash = expectedLocalHash,
            FieldPaths = ["name"]
        };

        var result = await service.ApplyFieldsAsync(sessionId, request, CancellationToken.None);

        result.AdoptedRevision.Should().Be(2);
        result.AppliedFieldPaths.Should().ContainSingle().Which.Should().Be("name");
        var updatedProduct = await context.Products.AsNoTracking().SingleAsync(value => value.Id == productId);
        updatedProduct.Name.Should().Be("Central name 2");
        updatedProduct.Description.Should().Be(localDescription);
        updatedProduct.BasePrice.Should().Be(8.75m);
        updatedProduct.Ingredients.Should().ContainSingle().Which.Should().Be("Tenant ingredient");
        updatedProduct.Allergens.Should().ContainSingle().Which.Should().Be("Tenant allergen");
        updatedProduct.IsActive.Should().BeFalse();
        updatedProduct.IsAvailable.Should().BeFalse();
        var updatedTranslation = await context.ProductDescriptions.AsNoTracking()
            .SingleAsync(value => value.ProductId == productId && value.Lang == "tr");
        updatedTranslation.Name.Should().Be(localTranslationName);
        updatedTranslation.Description.Should().Be(localTranslationDescription);

        var adoptedBaseline = await context.CatalogueTemplateAdoptions.AsNoTracking()
            .SingleAsync(value => value.AdoptionId == adoptionId && value.SourceTemplateId == "revision-item" &&
                value.SourceRevision == 2 && value.SourceEntryId == null);
        var baseline = CatalogueRevisionBaseline.Read(adoptedBaseline.BaselineFieldsJson,
            "item", 2, revisionTwo.ContentHash);
        baseline["name"].Value.Should().Be("Central name 2");
        baseline["description"].Value.Should().Be("Central description 1");

        var staleRequest = request with
        {
            ExpectedSessionVersion = result.SessionVersion,
            AdoptedRevision = 2,
            FieldPaths = ["description"]
        };
        var act = () => service.ApplyFieldsAsync(sessionId, staleRequest, CancellationToken.None);
        await act.Should().ThrowAsync<ConflictException>()
            .WithMessage("Tenant text changed after the revision preview. Reload before applying.");
    }

    [Fact]
    public async Task Apply_rejects_a_stale_session_version_without_mutating_the_tenant_record()
    {
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var currentUser = scope.ServiceProvider.GetRequiredService<ICurrentUserService>();
        var scenario = await SeedItemRevisionScenarioAsync(context);
        var catalogue = new Mock<ICentralCatalogueClient>();
        catalogue.Setup(value => value.GetCurrentRevisionBatchAsync(
                It.IsAny<IReadOnlyList<CatalogueCurrentRevisionRequest>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CurrentRevisionResponse(scenario.RevisionTwo));
        var service = new CatalogueRevisionChangeService(context, catalogue.Object, currentUser);
        var preview = await service.GetAsync(scenario.SessionId, CancellationToken.None);
        var request = ApplyRequest(preview, scenario.RevisionOne, scenario.RevisionTwo, ["name"]) with
        {
            ExpectedSessionVersion = preview.SessionVersion + 1
        };

        var act = () => service.ApplyFieldsAsync(scenario.SessionId, request, CancellationToken.None);

        await act.Should().ThrowAsync<ConflictException>()
            .WithMessage("Import session changed. Reload it before applying revision fields.");
        (await context.Products.AsNoTracking().SingleAsync(value => value.Id == scenario.ProductId))
            .Name.Should().Be("Tenant edited name");
        (await context.CatalogueImportSessions.AsNoTracking().SingleAsync(value => value.Id == scenario.SessionId))
            .Version.Should().Be(preview.SessionVersion);
        (await context.CatalogueTemplateAdoptions.AsNoTracking().Where(value =>
            value.AdoptionId == scenario.AdoptionId && value.SourceTemplateId == scenario.RevisionOne.TemplateId &&
            value.SourceEntryId == null).ToListAsync()).Should().ContainSingle();
    }

    [Fact]
    public async Task Apply_rejects_catalogue_revision_or_hash_changes_since_preview()
    {
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var currentUser = scope.ServiceProvider.GetRequiredService<ICurrentUserService>();
        var scenario = await SeedItemRevisionScenarioAsync(context);
        var changedRevision = Revision(3, "Central name 3", "Central description 3", "Central TR name 3");
        var changedHash = scenario.RevisionTwo with { ContentHash = new string('d', 64) };
        var catalogue = new Mock<ICentralCatalogueClient>();
        catalogue.SetupSequence(value => value.GetCurrentRevisionBatchAsync(
                It.IsAny<IReadOnlyList<CatalogueCurrentRevisionRequest>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CurrentRevisionResponse(scenario.RevisionTwo))
            .ReturnsAsync(CurrentRevisionResponse(changedRevision))
            .ReturnsAsync(CurrentRevisionResponse(changedHash));
        var service = new CatalogueRevisionChangeService(context, catalogue.Object, currentUser);
        var preview = await service.GetAsync(scenario.SessionId, CancellationToken.None);
        var request = ApplyRequest(preview, scenario.RevisionOne, scenario.RevisionTwo, ["name"]);

        var changedRevisionAct = () => service.ApplyFieldsAsync(scenario.SessionId, request, CancellationToken.None);

        await changedRevisionAct.Should().ThrowAsync<ConflictException>()
            .WithMessage("Catalogue revision changed. Reload revision changes.");
        var changedHashAct = () => service.ApplyFieldsAsync(scenario.SessionId, request, CancellationToken.None);
        await changedHashAct.Should().ThrowAsync<ConflictException>()
            .WithMessage("Catalogue revision changed. Reload revision changes.");

        (await context.Products.AsNoTracking().SingleAsync(value => value.Id == scenario.ProductId))
            .Name.Should().Be("Tenant edited name");
        (await context.CatalogueImportSessions.AsNoTracking().SingleAsync(value => value.Id == scenario.SessionId))
            .Version.Should().Be(preview.SessionVersion);
    }

    [Fact]
    public async Task Apply_rejects_unchanged_and_invalid_selected_field_paths()
    {
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var currentUser = scope.ServiceProvider.GetRequiredService<ICurrentUserService>();
        var scenario = await SeedItemRevisionScenarioAsync(context);
        var onlyNameChanged = scenario.RevisionTwo with
        {
            Description = scenario.RevisionOne.Description,
            Translations = scenario.RevisionOne.Translations
        };
        var catalogue = new Mock<ICentralCatalogueClient>();
        catalogue.Setup(value => value.GetCurrentRevisionBatchAsync(
                It.IsAny<IReadOnlyList<CatalogueCurrentRevisionRequest>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CurrentRevisionResponse(onlyNameChanged));
        var service = new CatalogueRevisionChangeService(context, catalogue.Object, currentUser);
        var preview = await service.GetAsync(scenario.SessionId, CancellationToken.None);
        var unchangedPath = ApplyRequest(preview, scenario.RevisionOne, onlyNameChanged, ["description"]);
        var invalidPath = unchangedPath with { FieldPaths = ["price"] };

        var unchangedAct = () => service.ApplyFieldsAsync(scenario.SessionId, unchangedPath, CancellationToken.None);
        await unchangedAct.Should().ThrowAsync<ConflictException>()
            .WithMessage("A selected field is no longer changed in the current revision.");
        var invalidAct = () => service.ApplyFieldsAsync(scenario.SessionId, invalidPath, CancellationToken.None);
        await invalidAct.Should().ThrowAsync<ConflictException>()
            .WithMessage("A selected field is no longer changed in the current revision.");

        (await context.Products.AsNoTracking().SingleAsync(value => value.Id == scenario.ProductId))
            .Name.Should().Be("Tenant edited name");
        (await context.CatalogueImportSessions.AsNoTracking().SingleAsync(value => value.Id == scenario.SessionId))
            .Version.Should().Be(preview.SessionVersion);
    }

    [Fact]
    public async Task Revision_change_endpoints_require_admin()
    {
        AuthenticateAsUser();
        var endpoint = $"/api/catalogue/import-sessions/{Guid.NewGuid()}/revision-changes";

        var getResponse = await Client.GetAsync(endpoint);
        var postResponse = await PostAsJsonAsync($"{endpoint}/apply", new ApplyCatalogueRevisionFieldsRequest
        {
            ExpectedSessionVersion = 1,
            TemplateId = "revision-item",
            AdoptedRevision = 1,
            CurrentRevision = 2,
            CurrentContentHash = new string('b', 64),
            ExpectedLocalHash = new string('a', 64),
            FieldPaths = ["name"]
        });

        getResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        postResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Applying_option_set_text_advances_its_version_once_and_normalizes_its_name()
    {
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var optionSetId = Guid.NewGuid();
        var optionSet = new OptionSet
        {
            Id = optionSetId,
            Kind = OptionSetKind.Ingredient,
            Name = "Central name 1",
            NormalizedName = "central name 1",
            SourceLocale = "en",
            Status = OptionSetStatus.Active,
            Version = 1,
            CreatedBy = "test"
        };
        optionSet.Translations.Add(new OptionSetTranslation
        {
            Id = Guid.NewGuid(),
            OptionSetId = optionSetId,
            LanguageCode = "tr",
            Name = "Merkezi ad 1",
            CreatedBy = "test"
        });
        context.OptionSets.Add(optionSet);
        await context.SaveChangesAsync();

        var revision = Revision(2, "Central name 2", "Unused description", "Merkezi ad 2") with
        {
            Type = "option-set"
        };
        var adoption = new CatalogueTemplateAdoption
        {
            Id = Guid.NewGuid(),
            AdoptionId = Guid.NewGuid(),
            SessionId = Guid.NewGuid(),
            SourceTemplateId = revision.TemplateId,
            SourceRevision = 1,
            LocalEntityType = "OptionSet",
            LocalEntityId = optionSetId,
            ContentHash = new string('a', 64),
            CreatedBy = "test"
        };
        var store = new CatalogueRevisionLocalTextStore(
            context, scope.ServiceProvider.GetRequiredService<ICurrentUserService>());

        await store.ApplyAsync(adoption, "option-set", revision, [], ["name", "translations[tr].name"],
            CancellationToken.None);
        await context.SaveChangesAsync();

        var updated = await context.OptionSets.AsNoTracking().Include(value => value.Translations)
            .SingleAsync(value => value.Id == optionSetId);
        updated.Name.Should().Be("Central name 2");
        updated.NormalizedName.Should().Be("central name 2");
        updated.Version.Should().Be(2);
        updated.UpdatedAt.Should().NotBeNull();
        updated.Translations.Single().Name.Should().Be("Merkezi ad 2");
    }

    [Fact]
    public async Task Removing_a_selected_translation_name_conflicts_without_deleting_local_description()
    {
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var productId = Guid.NewGuid();
        var product = new Product
        {
            Id = productId,
            Name = "Tenant product",
            BasePrice = 5,
            Type = ProductType.MainItem,
            KitchenType = KitchenType.BackKitchen,
            IsActive = false,
            IsAvailable = false,
            CreatedBy = "test"
        };
        var translation = new ProductDescription
        {
            Id = Guid.NewGuid(),
            ProductId = productId,
            Lang = "tr",
            Name = "Tenant translated name",
            Description = "Tenant-authored description",
            CreatedBy = "test"
        };
        product.Descriptions.Add(translation);
        context.Products.Add(product);
        await context.SaveChangesAsync();

        var revision = Revision(2, "Central name", "Central description", "Unused") with
        {
            Translations = new Dictionary<string, CentralCatalogueTranslation>()
        };
        var adoption = new CatalogueTemplateAdoption
        {
            Id = Guid.NewGuid(),
            AdoptionId = Guid.NewGuid(),
            SessionId = Guid.NewGuid(),
            SourceTemplateId = revision.TemplateId,
            SourceRevision = 1,
            LocalEntityType = "Product",
            LocalEntityId = productId,
            ContentHash = new string('a', 64),
            CreatedBy = "test"
        };
        var store = new CatalogueRevisionLocalTextStore(
            context, scope.ServiceProvider.GetRequiredService<ICurrentUserService>());

        var act = () => store.ApplyAsync(adoption, "item", revision, [],
            ["translations[tr].name"], CancellationToken.None);

        await act.Should().ThrowAsync<ConflictException>()
            .WithMessage("The source removed this translation name, but the tenant description has local text. Edit or clear it first.");
        var unchanged = await context.ProductDescriptions.AsNoTracking()
            .SingleAsync(value => value.ProductId == productId && value.Lang == "tr");
        unchanged.Name.Should().Be("Tenant translated name");
        unchanged.Description.Should().Be("Tenant-authored description");
    }

    [Fact]
    public async Task Applying_bundle_section_text_advances_the_menu_authoring_version_once()
    {
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var productId = Guid.NewGuid();
        var definitionId = Guid.NewGuid();
        var sectionId = Guid.NewGuid();
        var product = new Product
        {
            Id = productId,
            Name = "Bundle",
            BasePrice = 10,
            Type = ProductType.Menu,
            KitchenType = KitchenType.None,
            IsActive = false,
            IsAvailable = false,
            CreatedBy = "test"
        };
        var definition = new MenuDefinition
        {
            Id = definitionId,
            ProductId = productId,
            Product = product,
            AuthoringVersion = 1,
            CreatedBy = "test"
        };
        var section = new MenuSection
        {
            Id = sectionId,
            MenuDefinitionId = definitionId,
            MenuDefinition = definition,
            Name = "Drinks 1",
            CreatedBy = "test"
        };
        definition.Sections.Add(section);
        product.MenuDefinition = definition;
        context.Products.Add(product);
        await context.SaveChangesAsync();

        using var payload = JsonDocument.Parse("""
            {"sections":[{"sectionKey":"drinks","name":"Drinks 2","sortOrder":0,"min":0,"max":1,
              "options":[{"templateId":"drink-option","revision":1,"sortOrder":0,"default":false}],
              "translations":{"tr":{"name":"İçecekler 2"}}}]}
            """);
        var revision = new CentralCatalogueTemplateRevision
        {
            SchemaVersion = 1,
            TemplateId = "revision-bundle",
            Revision = 2,
            Type = "bundle",
            Name = "Bundle 2",
            SourceLocale = "en",
            Translations = new Dictionary<string, CentralCatalogueTranslation>(),
            Provenance = JsonSerializer.SerializeToElement(new { source = "test" }),
            QualityStatus = "reviewed",
            CompatibleTenantContractVersions = [1],
            Payload = payload.RootElement.Clone(),
            ContentHash = new string('b', 64)
        };
        var adoption = new CatalogueTemplateAdoption
        {
            Id = Guid.NewGuid(),
            AdoptionId = Guid.NewGuid(),
            SessionId = Guid.NewGuid(),
            SourceTemplateId = revision.TemplateId,
            SourceRevision = 1,
            LocalEntityType = "Product",
            LocalEntityId = productId,
            ContentHash = new string('a', 64),
            CreatedBy = "test"
        };
        var sectionMapping = new CatalogueTemplateAdoption
        {
            Id = Guid.NewGuid(),
            AdoptionId = adoption.AdoptionId,
            SessionId = adoption.SessionId,
            SourceTemplateId = adoption.SourceTemplateId,
            SourceRevision = adoption.SourceRevision,
            SourceEntryId = "drinks",
            LocalEntityType = "MenuSection",
            LocalEntityId = sectionId,
            ContentHash = adoption.ContentHash,
            CreatedBy = "test"
        };
        var store = new CatalogueRevisionLocalTextStore(
            context, scope.ServiceProvider.GetRequiredService<ICurrentUserService>());

        await store.ApplyAsync(adoption, "bundle", revision, [sectionMapping],
            ["sections[drinks].name", "sections[drinks].translations[tr].name"], CancellationToken.None);
        await context.SaveChangesAsync();

        var updatedDefinition = await context.MenuDefinitions.AsNoTracking().SingleAsync(value => value.Id == definitionId);
        var updatedSection = await context.MenuSections.AsNoTracking().Include(value => value.Translations)
            .SingleAsync(value => value.Id == sectionId);
        updatedDefinition.AuthoringVersion.Should().Be(2);
        updatedSection.Name.Should().Be("Drinks 2");
        updatedSection.Translations.Single().Name.Should().Be("İçecekler 2");
        updatedSection.UpdatedAt.Should().NotBeNull();
    }

    private static async Task<ItemRevisionScenario> SeedItemRevisionScenarioAsync(ApplicationDbContext context)
    {
        var sessionId = Guid.NewGuid();
        var adoptionId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var revisionOne = Revision(1, "Central name 1", "Central description 1", "Central TR name 1");
        var revisionTwo = Revision(2, "Central name 2", "Central description 2", "Central TR name 2");
        var product = new Product
        {
            Id = productId,
            Name = "Tenant edited name",
            Description = "Tenant edited description",
            BasePrice = 8.75m,
            Type = ProductType.MainItem,
            KitchenType = KitchenType.BackKitchen,
            Ingredients = ["Tenant ingredient"],
            Allergens = ["Tenant allergen"],
            IsActive = false,
            IsAvailable = false,
            CreatedBy = "test"
        };
        product.Descriptions.Add(new ProductDescription
        {
            Id = Guid.NewGuid(),
            ProductId = productId,
            Lang = "tr",
            Name = "Lokalde düzenlenen ad",
            Description = "Lokalde düzenlenen açıklama",
            CreatedBy = "test"
        });
        var session = new CatalogueImportSession
        {
            Id = sessionId,
            RootTemplateId = revisionOne.TemplateId,
            RootRevision = revisionOne.Revision,
            Locale = "tr",
            IdempotencyKey = $"revision-guard-test-{sessionId:N}",
            AdoptionId = adoptionId,
            Version = 1,
            CreatedBy = "test"
        };
        var adoption = new CatalogueTemplateAdoption
        {
            Id = Guid.NewGuid(),
            AdoptionId = adoptionId,
            SessionId = sessionId,
            SourceTemplateId = revisionOne.TemplateId,
            SourceRevision = revisionOne.Revision,
            LocalEntityType = "Product",
            LocalEntityId = productId,
            ContentHash = revisionOne.ContentHash,
            BaselineFieldsJson = CatalogueRevisionBaseline.Create(revisionOne, "item"),
            CreatedBy = "test"
        };

        context.Products.Add(product);
        context.CatalogueImportSessions.Add(session);
        context.CatalogueTemplateAdoptions.Add(adoption);
        await context.SaveChangesAsync();
        return new ItemRevisionScenario(sessionId, adoptionId, productId, revisionOne, revisionTwo);
    }

    private static CatalogueProxyResponse CurrentRevisionResponse(CentralCatalogueTemplateRevision revision) =>
        Response(StatusCodes.Status200OK, new
        {
            items = new[]
            {
                new
                {
                    templateId = revision.TemplateId,
                    status = "available",
                    revision,
                    adoptedRevisionWithdrawn = false
                }
            }
        });

    private sealed record ItemRevisionScenario(
        Guid SessionId,
        Guid AdoptionId,
        Guid ProductId,
        CentralCatalogueTemplateRevision RevisionOne,
        CentralCatalogueTemplateRevision RevisionTwo);

    private static CatalogueProxyResponse Response(int statusCode, object body)
    {
        var json = JsonSerializer.Serialize(body, JsonOptions);
        using var document = JsonDocument.Parse(json);
        return new CatalogueProxyResponse(statusCode, document.RootElement.Clone());
    }

    private static CentralCatalogueTemplateRevision Revision(
        int revision,
        string name,
        string description,
        string translatedName) => new()
        {
            SchemaVersion = 1,
            TemplateId = "revision-item",
            Revision = revision,
            Type = "item",
            Name = name,
            Description = description,
            SourceLocale = "en",
            Translations = new Dictionary<string, CentralCatalogueTranslation>(StringComparer.Ordinal)
            {
                ["tr"] = new() { Name = translatedName, Description = $"Central TR description {revision}" }
            },
            Provenance = JsonSerializer.SerializeToElement(new { source = "test" }),
            QualityStatus = "reviewed",
            CompatibleTenantContractVersions = [1],
            Payload = JsonSerializer.SerializeToElement(new { }),
            ContentHash = new string(revision == 1 ? 'a' : 'b', 64)
        };

    private static CentralCatalogueTemplateRevision CategoryRevision(string templateId, int revision) =>
        Revision(revision, $"Category {revision}", $"Description {revision}", "unused") with
        {
            TemplateId = templateId,
            Type = "category"
        };

    private static ApplyCatalogueRevisionFieldsRequest ApplyRequest(
        CatalogueRevisionChangesDto preview,
        CentralCatalogueTemplateRevision adopted,
        CentralCatalogueTemplateRevision current,
        List<string> fieldPaths) => new()
        {
            ExpectedSessionVersion = preview.SessionVersion,
            TemplateId = adopted.TemplateId,
            AdoptedRevision = adopted.Revision,
            CurrentRevision = current.Revision,
            CurrentContentHash = current.ContentHash,
            ExpectedLocalHash = preview.Items.Single().LocalHash!,
            FieldPaths = fieldPaths
        };

    private static CentralCatalogueTemplateRevision BundleRevision(int revision)
    {
        var sectionName = $"Drinks {revision}";
        var translatedName = $"İçecekler {revision}";
        return new CentralCatalogueTemplateRevision
        {
            SchemaVersion = 1,
            TemplateId = "revision-bundle",
            Revision = revision,
            Type = "bundle",
            Name = $"Bundle {revision}",
            SourceLocale = "en",
            Provenance = JsonSerializer.SerializeToElement(new { source = "test" }),
            QualityStatus = "reviewed",
            CompatibleTenantContractVersions = [1],
            Payload = JsonSerializer.SerializeToElement(new
            {
                sections = new[]
                {
                    new
                    {
                        sectionKey = "drinks",
                        name = sectionName,
                        sortOrder = 0,
                        min = 0,
                        max = 1,
                        options = new[]
                        {
                            new { templateId = "drink-option", revision = 1, sortOrder = 0, @default = false }
                        },
                        translations = new Dictionary<string, object>
                        {
                            ["tr"] = new { name = translatedName }
                        }
                    }
                }
            }, JsonOptions),
            ContentHash = new string(revision switch { 1 => 'a', 2 => 'b', _ => 'c' }, 64)
        };
    }

    private static CatalogueTemplateAdoption Adoption(
        CatalogueImportSession session,
        string templateId,
        CentralCatalogueTemplateRevision revision,
        Guid categoryId) => new()
        {
            Id = Guid.NewGuid(),
            AdoptionId = session.AdoptionId,
            SessionId = session.Id,
            SourceTemplateId = templateId,
            SourceRevision = revision.Revision,
            LocalEntityType = "Category",
            LocalEntityId = categoryId,
            ContentHash = revision.ContentHash,
            BaselineFieldsJson = CatalogueRevisionBaseline.Create(revision, "category"),
            CreatedBy = "test"
        };
}
