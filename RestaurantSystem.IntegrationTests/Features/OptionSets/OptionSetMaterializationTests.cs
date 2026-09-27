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
    private static readonly Guid SecondSectionId = Guid.NewGuid();
    private static readonly Guid ChoiceGroupId = Guid.NewGuid();
    private static readonly Guid ExistingChoiceId = Guid.NewGuid();
    private static readonly Guid AddedChoiceId = Guid.NewGuid();
    private static readonly Guid ThirdChoiceId = Guid.NewGuid();
    private static readonly Guid SauceId = Guid.NewGuid();
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
    public async Task Update_keeps_a_new_entry_enabled_and_materializable()
    {
        AuthenticateAsAdmin();
        var detail = (await CreateBundleChoiceSetAsync())!.Data!;
        var update = new OptionSetWriteRequestDto
        {
            Kind = detail.Kind,
            Name = detail.Name,
            SourceLocale = detail.SourceLocale,
            Translations = detail.Translations,
            Entries =
            [
                .. detail.Entries,
                new OptionSetEntryDto { Name = "Beef", ProductId = ThirdChoiceId, DisplayOrder = 2 }
            ]
        };
        Client.DefaultRequestHeaders.TryAddWithoutValidation("If-Match", "\"1\"");

        var updateResponse = await PutAsJsonAsync($"/api/OptionSets/{detail.Id}", update);
        updateResponse.StatusCode.Should().Be(HttpStatusCode.OK,
            await updateResponse.Content.ReadAsStringAsync());
        var updated = (await ReadResponseAsync<ApiResponse<OptionSetDetailDto>>(updateResponse))!.Data!;
        updated.Entries.Should().ContainSingle(entry => entry.ProductId == ThirdChoiceId);
        updated.Entries.Single(entry => entry.ProductId == ThirdChoiceId).Id.Should().NotBe(Guid.Empty);

        var request = new OptionSetMaterializationRequest
        {
            OptionSetId = updated.Id,
            ExpectedSetVersion = updated.Version,
            IdempotencyKey = $"added-entry-{Guid.NewGuid():N}",
            Targets =
            [
                new OptionSetMaterializationTargetRequest
                {
                    TargetKey = "taco-menu-choice",
                    Role = OptionSetAttachmentRole.BundleChoice,
                    TargetProductId = MenuId,
                    TargetMenuSectionId = SectionId,
                    ExpectedMenuAuthoringVersion = 1,
                    IntentionalDifferenceReason = "The related standalone offer keeps its local choice group.",
                    Settings = new OptionSetAttachmentSettings { MinSelection = 1, MaxSelection = 2 }
                }
            ]
        };
        var applyResponse = await PostAsJsonAsync($"/api/OptionSets/{updated.Id}/apply", request);
        applyResponse.StatusCode.Should().Be(HttpStatusCode.OK,
            await applyResponse.Content.ReadAsStringAsync());
        var applied = (await ReadResponseAsync<ApiResponse<OptionSetMaterializationResult>>(applyResponse))!.Data!;
        applied.Targets.Should().ContainSingle().Which.Status.Should().Be("applied");

        await using var context = DatabaseFixture.CreateContext();
        var section = await context.MenuSections.Include(item => item.Items)
            .SingleAsync(item => item.Id == SectionId);
        section.Items.Should().Contain(item => item.ProductId == ThirdChoiceId);
    }

    [Fact]
    public async Task Updating_metadata_allows_existing_inactive_reference_but_rejects_a_new_one()
    {
        AuthenticateAsAdmin();
        var detail = (await CreateBundleChoiceSetAsync())!.Data!;
        await using (var context = DatabaseFixture.CreateContext())
        {
            var existing = await context.Products.SingleAsync(product => product.Id == ExistingChoiceId);
            var newReference = await context.Products.SingleAsync(product => product.Id == ThirdChoiceId);
            existing.IsActive = false;
            existing.IsAvailable = false;
            newReference.IsActive = false;
            newReference.IsAvailable = false;
            await context.SaveChangesAsync();
        }

        var rename = new OptionSetWriteRequestDto
        {
            Kind = detail.Kind,
            Name = "Taco proteins translated",
            SourceLocale = "en",
            Translations = new Dictionary<string, string> { ["en"] = "Taco proteins translated" },
            Entries = detail.Entries
        };
        Client.DefaultRequestHeaders.TryAddWithoutValidation("If-Match", "\"1\"");
        var renameResponse = await PutAsJsonAsync($"/api/OptionSets/{detail.Id}", rename);
        renameResponse.StatusCode.Should().Be(HttpStatusCode.OK,
            await renameResponse.Content.ReadAsStringAsync());
        var renamed = (await ReadResponseAsync<ApiResponse<OptionSetDetailDto>>(renameResponse))!.Data!;
        renamed.Version.Should().Be(2);
        renamed.Entries.Should().HaveCount(2);

        Client.DefaultRequestHeaders.Remove("If-Match");
        Client.DefaultRequestHeaders.TryAddWithoutValidation("If-Match", "\"2\"");
        var addInactive = new OptionSetWriteRequestDto
        {
            Kind = renamed.Kind,
            Name = renamed.Name,
            SourceLocale = renamed.SourceLocale,
            Translations = renamed.Translations,
            Entries =
            [
                .. renamed.Entries,
                new OptionSetEntryDto { Name = "Unavailable beef", ProductId = ThirdChoiceId, DisplayOrder = 2 }
            ]
        };
        var rejected = await PutAsJsonAsync($"/api/OptionSets/{detail.Id}", addInactive);
        rejected.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            await rejected.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Sauce_preview_shows_rule_only_change_and_can_clear_the_existing_maximum()
    {
        AuthenticateAsAdmin();
        var create = new OptionSetWriteRequestDto
        {
            Kind = OptionSetKind.Sauce,
            Name = "Hot sauce choices",
            SourceLocale = "en",
            Translations = new Dictionary<string, string> { ["en"] = "Hot sauce choices" },
            Entries = [new OptionSetEntryDto { Name = "Hot sauce", GlobalIngredientId = SauceId }]
        };
        var createResponse = await PostAsJsonAsync("/api/OptionSets", create);
        createResponse.StatusCode.Should().Be(HttpStatusCode.OK,
            await createResponse.Content.ReadAsStringAsync());
        var set = (await ReadResponseAsync<ApiResponse<OptionSetDetailDto>>(createResponse))!.Data!;
        var initial = new OptionSetMaterializationRequest
        {
            OptionSetId = set.Id,
            ExpectedSetVersion = set.Version,
            IdempotencyKey = $"sauce-initial-{Guid.NewGuid():N}",
            Targets =
            [
                new OptionSetMaterializationTargetRequest
                {
                    TargetKey = "falafel-sauces",
                    Role = OptionSetAttachmentRole.Sauce,
                    TargetProductId = AddedChoiceId,
                    Settings = new OptionSetAttachmentSettings
                    {
                        MinSelection = 0, MaxSelection = 1, IncludedFree = 0, DisplayOrder = 0
                    }
                }
            ]
        };
        var initialResponse = await PostAsJsonAsync($"/api/OptionSets/{set.Id}/apply", initial);
        initialResponse.StatusCode.Should().Be(HttpStatusCode.OK,
            await initialResponse.Content.ReadAsStringAsync());
        (await ReadResponseAsync<ApiResponse<OptionSetMaterializationResult>>(initialResponse))!
            .Data!.Targets.Should().ContainSingle().Which.Status.Should().Be("applied");

        var clearMaximum = new OptionSetMaterializationRequest
        {
            OptionSetId = set.Id,
            ExpectedSetVersion = set.Version,
            IdempotencyKey = $"sauce-clear-{Guid.NewGuid():N}",
            Targets =
            [
                new OptionSetMaterializationTargetRequest
                {
                    TargetKey = "falafel-sauces",
                    Role = OptionSetAttachmentRole.Sauce,
                    TargetProductId = AddedChoiceId,
                    ExpectedAttachmentVersion = 1,
                    Settings = new OptionSetAttachmentSettings
                    {
                        MinSelection = 0, ClearMaxSelection = true, IncludedFree = 0, DisplayOrder = 0
                    }
                }
            ]
        };
        var previewResponse = await PostAsJsonAsync($"/api/OptionSets/{set.Id}/preview", clearMaximum);
        previewResponse.StatusCode.Should().Be(HttpStatusCode.OK,
            await previewResponse.Content.ReadAsStringAsync());
        var preview = (await ReadResponseAsync<ApiResponse<OptionSetMaterializationPreview>>(previewResponse))!.Data!;
        var targetPreview = preview.Targets.Should().ContainSingle().Which;
        targetPreview.Status.Should().Be("ready");
        targetPreview.CurrentSettings.MaxSelection.Should().Be(1);
        targetPreview.ProposedSettings.MaxSelection.Should().BeNull();
        targetPreview.ChangedSettings.Should().ContainSingle().Which.Should().Be("maxSelection");

        var applyResponse = await PostAsJsonAsync($"/api/OptionSets/{set.Id}/apply", clearMaximum);
        applyResponse.StatusCode.Should().Be(HttpStatusCode.OK,
            await applyResponse.Content.ReadAsStringAsync());
        var applied = (await ReadResponseAsync<ApiResponse<OptionSetMaterializationResult>>(applyResponse))!.Data!;
        applied.Targets.Should().ContainSingle().Which.Status.Should().Be("applied");

        await using var context = DatabaseFixture.CreateContext();
        (await context.Products.Where(product => product.Id == AddedChoiceId)
            .Select(product => product.SauceMax).SingleAsync()).Should().BeNull();
        (await context.OptionSetAttachments.Where(attachment => attachment.OptionSetId == set.Id)
            .Select(attachment => attachment.MaxSelection).SingleAsync()).Should().BeNull();
    }

    [Fact]
    public async Task Long_import_ids_get_bounded_unique_names_and_staged_inactive_targets_stay_unavailable()
    {
        AuthenticateAsAdmin();
        var adminSet = (await CreateBundleChoiceSetAsync())!.Data!;
        await using (var stateContext = DatabaseFixture.CreateContext())
        {
            var menu = await stateContext.Products.SingleAsync(product => product.Id == MenuId);
            var referencedChoice = await stateContext.Products.SingleAsync(product => product.Id == AddedChoiceId);
            menu.IsActive = false;
            menu.IsAvailable = false;
            referencedChoice.IsActive = false;
            referencedChoice.IsAvailable = false;
            await stateContext.SaveChangesAsync();
        }

        var adminApply = new OptionSetMaterializationRequest
        {
            OptionSetId = adminSet.Id,
            ExpectedSetVersion = adminSet.Version,
            IdempotencyKey = $"admin-inactive-{Guid.NewGuid():N}",
            Targets = [BundleTarget("inactive-admin-target", SectionId, 1)]
        };
        var adminResponse = await PostAsJsonAsync($"/api/OptionSets/{adminSet.Id}/apply", adminApply);
        adminResponse.StatusCode.Should().Be(HttpStatusCode.OK,
            await adminResponse.Content.ReadAsStringAsync());
        var adminResult = (await ReadResponseAsync<ApiResponse<OptionSetMaterializationResult>>(adminResponse))!.Data!;
        adminResult.Targets.Should().ContainSingle().Which.Status.Should().Be("conflict");

        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var materializer = scope.ServiceProvider.GetRequiredService<IOptionSetMaterializer>();
        await using var transaction = await context.Database.BeginTransactionAsync();
        var longSourceIdOne = new string('s', 119) + "1";
        var longSourceIdTwo = new string('s', 119) + "2";
        var stagedProducts = new HashSet<Guid> { MenuId, AddedChoiceId };
        var firstImported = await materializer.CreateOrReuseImportedSetAsync(
            ImportedSetRequest("menu-catalogue-test-one", longSourceIdOne, adminSet.Name, stagedProducts),
            CancellationToken.None);
        var secondImported = await materializer.CreateOrReuseImportedSetAsync(
            ImportedSetRequest("menu-catalogue-test-two", longSourceIdTwo, adminSet.Name, stagedProducts),
            CancellationToken.None);

        var importedSets = await context.OptionSets.AsNoTracking()
            .Where(set => set.Id == firstImported.OptionSetId || set.Id == secondImported.OptionSetId)
            .OrderBy(set => set.Id)
            .ToListAsync();
        importedSets.Should().HaveCount(2);
        importedSets.Should().OnlyContain(set => set.Name.Length <= 120);
        importedSets.Select(set => set.NormalizedName).Distinct().Should().HaveCount(2);

        var importApply = new OptionSetMaterializationRequest
        {
            OptionSetId = firstImported.OptionSetId,
            ExpectedSetVersion = firstImported.Version,
            IdempotencyKey = $"staged-product-{Guid.NewGuid():N}",
            Targets = [BundleTarget("imported-inactive-menu", SectionId, 1)]
        };
        var importResult = await materializer.ApplyImportedAsync(
            importApply, stagedProducts, CancellationToken.None);
        importResult.Targets.Should().ContainSingle().Which.Status.Should().Be("applied");
        await transaction.CommitAsync();

        await using var verify = DatabaseFixture.CreateContext();
        var stagedMenu = await verify.Products.SingleAsync(product => product.Id == MenuId);
        var stagedChoice = await verify.Products.SingleAsync(product => product.Id == AddedChoiceId);
        stagedMenu.IsActive.Should().BeFalse();
        stagedMenu.IsAvailable.Should().BeFalse();
        stagedChoice.IsActive.Should().BeFalse();
        stagedChoice.IsAvailable.Should().BeFalse();
        (await verify.MenuSections.Where(section => section.Id == SectionId)
            .SelectMany(section => section.Items).AnyAsync(item => item.ProductId == AddedChoiceId)).Should().BeTrue();
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
    public async Task Same_menu_retry_uses_the_actual_version_delta_from_an_idempotent_target()
    {
        AuthenticateAsAdmin();
        var set = (await CreateBundleChoiceSetAsync())!.Data!;
        await using (var context = DatabaseFixture.CreateContext())
        {
            var definition = await context.MenuDefinitions.SingleAsync(item => item.ProductId == MenuId);
            var secondSection = new MenuSection
            {
                Id = SecondSectionId,
                MenuDefinitionId = definition.Id,
                MenuDefinition = definition,
                Name = "Choose a second filling",
                DisplayOrder = 1,
                IsRequired = true,
                MinSelection = 1,
                MaxSelection = 2,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = Actor
            };
            secondSection.Items.Add(new MenuSectionItem
            {
                Id = Guid.NewGuid(),
                MenuSectionId = SecondSectionId,
                MenuSection = secondSection,
                ProductId = ExistingChoiceId,
                DisplayOrder = 0,
                IsDefault = true,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = Actor
            });
            context.MenuSections.Add(secondSection);
            await context.SaveChangesAsync();
        }

        var sharedKey = $"first-section-{Guid.NewGuid():N}";
        var first = new OptionSetMaterializationRequest
        {
            OptionSetId = set.Id,
            ExpectedSetVersion = set.Version,
            IdempotencyKey = sharedKey,
            Targets = [BundleTarget("first", SectionId, 1)]
        };
        var firstResponse = await PostAsJsonAsync($"/api/OptionSets/{set.Id}/apply", first);
        firstResponse.StatusCode.Should().Be(HttpStatusCode.OK,
            await firstResponse.Content.ReadAsStringAsync());

        var second = new OptionSetMaterializationRequest
        {
            OptionSetId = set.Id,
            ExpectedSetVersion = set.Version,
            IdempotencyKey = $"second-section-{Guid.NewGuid():N}",
            Targets = [BundleTarget("second", SecondSectionId, 2)]
        };
        var secondResponse = await PostAsJsonAsync($"/api/OptionSets/{set.Id}/apply", second);
        secondResponse.StatusCode.Should().Be(HttpStatusCode.OK,
            await secondResponse.Content.ReadAsStringAsync());
        (await ReadResponseAsync<ApiResponse<OptionSetMaterializationResult>>(secondResponse))!
            .Data!.Targets.Should().ContainSingle().Which.MenuAuthoringVersion.Should().Be(3);

        var retry = new OptionSetMaterializationRequest
        {
            OptionSetId = set.Id,
            ExpectedSetVersion = set.Version,
            IdempotencyKey = sharedKey,
            Targets =
            [
                BundleTarget("first", SectionId, 1, expectedAttachmentVersion: 1),
                BundleTarget("second", SecondSectionId, 1, expectedAttachmentVersion: 1)
            ]
        };
        var retryResponse = await PostAsJsonAsync($"/api/OptionSets/{set.Id}/apply", retry);
        retryResponse.StatusCode.Should().Be(HttpStatusCode.OK,
            await retryResponse.Content.ReadAsStringAsync());
        var retried = (await ReadResponseAsync<ApiResponse<OptionSetMaterializationResult>>(retryResponse))!.Data!;
        retried.Targets.Single(target => target.TargetKey == "first").Status.Should().Be("unchanged");
        var secondRetried = retried.Targets.Single(target => target.TargetKey == "second");
        secondRetried.Status.Should().Be("applied", string.Join("; ", secondRetried.Conflicts.Select(item => item.Message)));
        retried.Targets.Single(target => target.TargetKey == "second").MenuAuthoringVersion.Should().Be(4);

        await using var verify = DatabaseFixture.CreateContext();
        (await verify.MenuDefinitions.Where(item => item.ProductId == MenuId)
            .Select(item => item.AuthoringVersion).SingleAsync()).Should().Be(4);
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
        addedChoice.SauceMax = 1;
        var thirdChoice = Product(ThirdChoiceId, "Beef", 7m);
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
        context.AddRange(existingChoice, addedChoice, thirdChoice, standaloneProduct, choiceGroup, menuProduct, definition);
        context.GlobalIngredients.Add(new GlobalIngredient
        {
            Id = SauceId,
            DefaultName = "Hot sauce",
            Kind = IngredientKind.Sauce,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = Actor
        });
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

    private static OptionSetMaterializationTargetRequest BundleTarget(
        string key,
        Guid sectionId,
        int expectedMenuVersion,
        int? expectedAttachmentVersion = null) => new()
        {
            TargetKey = key,
            Role = OptionSetAttachmentRole.BundleChoice,
            TargetProductId = MenuId,
            TargetMenuSectionId = sectionId,
            ExpectedMenuAuthoringVersion = expectedMenuVersion,
            ExpectedAttachmentVersion = expectedAttachmentVersion,
            IntentionalDifferenceReason = "The linked standalone offer retains its existing choice group.",
            Settings = new OptionSetAttachmentSettings { MinSelection = 1, MaxSelection = 2, DisplayOrder = 0 }
        };

    private static CreateOrReuseImportedSetRequest ImportedSetRequest(
        string templateId,
        string sourceOptionSetId,
        string name,
        IReadOnlySet<Guid> stagedProductIds) => new()
        {
            SourceTemplateId = templateId,
            SourceRevision = 1,
            SourceOptionSetId = sourceOptionSetId,
            Kind = OptionSetKind.BundleChoice,
            Name = name,
            SourceLocale = "en",
            Translations = new Dictionary<string, string> { ["en"] = name },
            StagedProductIds = stagedProductIds,
            Entries =
        [
            new ImportedOptionSetEntryRequest
            {
                SourceEntryId = "chicken",
                Name = "Chicken",
                ProductId = ExistingChoiceId,
                IsDefault = true
            },
            new ImportedOptionSetEntryRequest
            {
                SourceEntryId = "falafel",
                Name = "Falafel",
                ProductId = AddedChoiceId,
                DisplayOrder = 1
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
