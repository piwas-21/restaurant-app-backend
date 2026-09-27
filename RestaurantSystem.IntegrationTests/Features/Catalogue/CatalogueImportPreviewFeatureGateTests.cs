using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using RestaurantSystem.Api.Common.TenantFeatures;
using RestaurantSystem.Api.Features.Catalogue.Dtos;
using RestaurantSystem.Api.Features.Catalogue.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Catalogue;

[Collection("Database Lane 3")]
public sealed class CatalogueImportPreviewFeatureGateTests(DatabaseFixture databaseFixture)
    : IntegrationTestBase(databaseFixture)
{
    [Fact]
    public async Task Preview_finds_existing_case_insensitive_candidates_in_a_batched_lookup()
    {
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var suffix = Guid.NewGuid().ToString("N");
        var category = new Category
        {
            Id = Guid.NewGuid(),
            Name = $"Batch candidate {suffix}",
            IsActive = true,
            DisplayOrder = 1,
            CreatedBy = "test"
        };
        var revision = new CentralCatalogueTemplateRevision
        {
            SchemaVersion = 1,
            TemplateId = $"batch-category-{suffix}",
            Revision = 1,
            Type = "category",
            Name = $"batch candidate {suffix}",
            SourceLocale = "en",
            QualityStatus = "reviewed",
            CompatibleTenantContractVersions = [1],
            Provenance = JsonSerializer.SerializeToElement(new { source = "test" }),
            Payload = JsonSerializer.SerializeToElement(new { sortOrder = 1 }),
            ContentHash = new string('b', 64)
        };
        var template = new CatalogueImportSessionTemplate
        {
            Id = Guid.NewGuid(),
            TemplateId = revision.TemplateId,
            Revision = revision.Revision,
            Type = revision.Type,
            ContentHash = revision.ContentHash,
            RevisionJson = JsonSerializer.Serialize(revision, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
            IsRoot = true,
            IsSelectable = true,
            IsSelected = true,
            Status = CatalogueImportItemStatus.Pending,
            CreatedBy = "test"
        };
        var session = new CatalogueImportSession
        {
            Id = Guid.NewGuid(),
            RootTemplateId = template.TemplateId,
            RootRevision = template.Revision,
            Locale = "en",
            IdempotencyKey = $"preview-candidate-{suffix}",
            AdoptionId = Guid.NewGuid(),
            Version = 1,
            CreatedBy = "test",
            Templates = [template]
        };
        context.Categories.Add(category);
        context.CatalogueImportSessions.Add(session);
        await context.SaveChangesAsync();

        var enabled = new Mock<ITenantFeatures>();
        enabled.SetupGet(value => value.OptionSetMaterializationEnabled).Returns(true);
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<CatalogueImportPreviewService>>();
        var preview = await new CatalogueImportPreviewService(context, enabled.Object, logger)
            .PreviewAsync(session.Id, CancellationToken.None);

        preview.Items.Should().ContainSingle();
        preview.Items.Single().Candidates.Should().ContainSingle(candidate =>
            candidate.EntityType == "Category" && candidate.Id == category.Id);
    }

    [Fact]
    public async Task Set_bearing_item_preview_is_gated_by_the_feature_and_dependency_review()
    {
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<CatalogueImportPreviewService>>();
        var revision = new CentralCatalogueTemplateRevision
        {
            SchemaVersion = 1,
            TemplateId = "import-item",
            Revision = 1,
            Type = "item",
            Name = "Reviewed source item",
            SourceLocale = "en",
            QualityStatus = "reviewed",
            CompatibleTenantContractVersions = [1],
            Provenance = JsonSerializer.SerializeToElement(new { source = "test" }),
            Payload = JsonSerializer.SerializeToElement(new
            {
                category = (object?)null,
                suggestedIngredients = Array.Empty<object>(),
                optionSets = new[] { new { templateId = "choice-set", revision = 1 } },
                sideSets = Array.Empty<object>()
            }),
            ContentHash = new string('a', 64)
        };
        var template = new CatalogueImportSessionTemplate
        {
            Id = Guid.NewGuid(),
            TemplateId = revision.TemplateId,
            Revision = revision.Revision,
            Type = revision.Type,
            ContentHash = revision.ContentHash,
            RevisionJson = JsonSerializer.Serialize(revision, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
            IsRoot = true,
            IsSelectable = true,
            IsSelected = true,
            DecisionJson = JsonSerializer.Serialize(new CatalogueImportItemDecision { Resolution = "Create" }),
            Status = CatalogueImportItemStatus.Pending,
            CreatedBy = "test"
        };
        var session = new CatalogueImportSession
        {
            Id = Guid.NewGuid(),
            RootTemplateId = template.TemplateId,
            RootRevision = template.Revision,
            Locale = "en",
            IdempotencyKey = $"preview-gate-{Guid.NewGuid():N}",
            AdoptionId = Guid.NewGuid(),
            Version = 1,
            CreatedBy = "test",
            Templates = [template]
        };
        context.CatalogueImportSessions.Add(session);
        await context.SaveChangesAsync();

        var disabled = new Mock<ITenantFeatures>();
        disabled.SetupGet(value => value.OptionSetMaterializationEnabled).Returns(false);
        var disabledPreview = await new CatalogueImportPreviewService(context, disabled.Object, logger)
            .PreviewAsync(session.Id, CancellationToken.None);

        disabledPreview.Items.Single().BlockingIssues.Should().ContainSingle(issue =>
            issue.Code == "OPTION_SET_MATERIALIZATION_DISABLED");

        var enabled = new Mock<ITenantFeatures>();
        enabled.SetupGet(value => value.OptionSetMaterializationEnabled).Returns(true);
        var enabledPreview = await new CatalogueImportPreviewService(context, enabled.Object, logger)
            .PreviewAsync(session.Id, CancellationToken.None);

        enabledPreview.Items.Single().BlockingIssues.Should().Contain(issue =>
            issue.Code == "DEPENDENCY_NOT_IN_SESSION");
        enabledPreview.Items.Single().BlockingIssues.Should().NotContain(issue =>
            issue.Code == "OPTION_SET_MATERIALIZATION_DISABLED" ||
            issue.Code == "OPTION_SET_MATERIALIZATION_UNAVAILABLE");
    }

    [Fact]
    public async Task Option_set_and_bundle_create_preview_reports_dependencies_not_missing_executor_support()
    {
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<CatalogueImportPreviewService>>();
        var enabled = new Mock<ITenantFeatures>();
        enabled.SetupGet(value => value.OptionSetMaterializationEnabled).Returns(true);
        var preview = new CatalogueImportPreviewService(context, enabled.Object, logger);
        var disabled = new Mock<ITenantFeatures>();
        disabled.SetupGet(value => value.OptionSetMaterializationEnabled).Returns(false);
        var disabledPreview = new CatalogueImportPreviewService(context, disabled.Object, logger);

        foreach (var type in new[] { "option-set", "bundle" })
        {
            using var payload = JsonDocument.Parse(type == "option-set"
                ? """{"kind":"sauce","min":0,"max":1,"options":[{"templateId":"choice","revision":1,"sortOrder":0}]}"""
                : """{"sections":[{"sectionKey":"choice","name":"Choice","sortOrder":0,"min":0,"max":1,"options":[{"templateId":"choice","revision":1,"sortOrder":0}]}]}""");
            var revision = new CentralCatalogueTemplateRevision
            {
                SchemaVersion = 1,
                TemplateId = $"blocked-{type}-{Guid.NewGuid():N}",
                Revision = 1,
                Type = type,
                Name = "Reviewed template",
                SourceLocale = "en",
                QualityStatus = "reviewed",
                CompatibleTenantContractVersions = [1],
                Provenance = JsonSerializer.SerializeToElement(new { source = "test" }),
                Payload = payload.RootElement.Clone(),
                ContentHash = new string('c', 64)
            };
            var item = new CatalogueImportSessionTemplate
            {
                Id = Guid.NewGuid(),
                TemplateId = revision.TemplateId,
                Revision = revision.Revision,
                Type = type,
                ContentHash = revision.ContentHash,
                RevisionJson = JsonSerializer.Serialize(revision, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
                IsRoot = true,
                IsSelectable = true,
                IsSelected = true,
                DecisionJson = JsonSerializer.Serialize(new CatalogueImportItemDecision
                {
                    Resolution = "Create",
                    ChoiceRulesReviewed = true,
                    OptionPricesReviewed = true,
                    LocalOptionPrices = new Dictionary<string, decimal> { ["choice@1"] = -1m }
                }),
                Status = CatalogueImportItemStatus.Pending,
                CreatedBy = "test"
            };
            var session = new CatalogueImportSession
            {
                Id = Guid.NewGuid(),
                RootTemplateId = item.TemplateId,
                RootRevision = item.Revision,
                Locale = "en",
                IdempotencyKey = $"preview-block-{Guid.NewGuid():N}",
                AdoptionId = Guid.NewGuid(),
                Version = 1,
                CreatedBy = "test",
                Templates = [item]
            };
            context.CatalogueImportSessions.Add(session);
            await context.SaveChangesAsync();

            var result = await preview.PreviewAsync(session.Id, CancellationToken.None);

            result.Items.Single().BlockingIssues.Should().Contain(issue => issue.Code == "DEPENDENCY_NOT_IN_SESSION");
            result.Items.Single().BlockingIssues.Should().Contain(issue => issue.Code == "OPTION_PRICE_INVALID");
            result.Items.Single().BlockingIssues.Should().NotContain(issue =>
                issue.Code == "OPTION_SET_MATERIALIZATION_DISABLED" ||
                issue.Code == "OPTION_SET_MATERIALIZATION_UNAVAILABLE");

            var blocked = await disabledPreview.PreviewAsync(session.Id, CancellationToken.None);
            blocked.Items.Single().BlockingIssues.Should().Contain(issue =>
                issue.Code == "OPTION_SET_MATERIALIZATION_DISABLED");
        }
    }
}
