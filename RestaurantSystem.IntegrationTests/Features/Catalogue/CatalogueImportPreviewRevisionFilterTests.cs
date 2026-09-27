using System.Data.Common;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
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
public sealed class CatalogueImportPreviewRevisionFilterTests(DatabaseFixture databaseFixture)
    : IntegrationTestBase(databaseFixture)
{
    [Fact]
    public async Task Preview_queries_only_selected_revisions_for_mappings_and_rejected_matches()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var templateId = $"revision-filter-{suffix}";
        var localCategory = new Category { Name = $"Candidate {suffix}", CreatedBy = "test" };
        var revision = new CentralCatalogueTemplateRevision
        {
            SchemaVersion = 1,
            TemplateId = templateId,
            Revision = 31,
            Type = "category",
            Name = localCategory.Name,
            SourceLocale = "en",
            QualityStatus = "reviewed",
            CompatibleTenantContractVersions = [1],
            Provenance = JsonSerializer.SerializeToElement(new { source = "test" }),
            Payload = JsonSerializer.SerializeToElement(new { sortOrder = 1 }),
            ContentHash = new string('d', 64)
        };
        var template = new CatalogueImportSessionTemplate
        {
            Id = Guid.NewGuid(),
            TemplateId = templateId,
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
            RootTemplateId = templateId,
            RootRevision = revision.Revision,
            Locale = "en",
            IdempotencyKey = $"revision-filter-{suffix}",
            AdoptionId = Guid.NewGuid(),
            Version = 1,
            CreatedBy = "test",
            Templates = [template]
        };
        var historicalMappings = Enumerable.Range(1, 30).Select(sourceRevision => new CatalogueTemplateAdoption
        {
            SessionId = session.Id,
            AdoptionId = Guid.NewGuid(),
            SourceTemplateId = templateId,
            SourceRevision = sourceRevision,
            LocalEntityType = "Category",
            LocalEntityId = Guid.NewGuid(),
            ContentHash = new string('e', 64),
            IsDefault = true,
            CreatedBy = "test"
        }).ToArray();
        var historicalRejections = Enumerable.Range(1, 30).Select(sourceRevision => new CatalogueMatchDecision
        {
            SourceTemplateId = templateId,
            SourceRevision = sourceRevision,
            NormalizedName = localCategory.Name.ToLowerInvariant(),
            CandidateType = "Category",
            CandidateId = localCategory.Id,
            Decision = CatalogueMatchDecisionStatus.Rejected,
            CreatedBy = "test"
        }).ToArray();
        await using (var seed = DatabaseFixture.CreateContext())
        {
            seed.Categories.Add(localCategory);
            seed.CatalogueImportSessions.Add(session);
            seed.CatalogueTemplateAdoptions.AddRange(historicalMappings);
            seed.CatalogueMatchDecisions.AddRange(historicalRejections);
            await seed.SaveChangesAsync();
        }

        var capture = new CatalogueReadCapture();
        await using var context = DatabaseFixture.CreateContext(capture);
        var features = new Mock<ITenantFeatures>();
        features.SetupGet(value => value.OptionSetMaterializationEnabled).Returns(false);
        var preview = new CatalogueImportPreviewService(
            context, features.Object, NullLogger<CatalogueImportPreviewService>.Instance);

        var result = await preview.PreviewAsync(session.Id, CancellationToken.None);

        var item = result.Items.Should().ContainSingle().Which;
        item.LocalEntityId.Should().BeNull();
        item.Candidates.Should().ContainSingle(candidate => candidate.Id == localCategory.Id);
        capture.Commands.Should().Contain(command =>
            command.Contains("catalogue_template_adoptions", StringComparison.OrdinalIgnoreCase) &&
            command.Contains("source_revision", StringComparison.OrdinalIgnoreCase));
        capture.Commands.Should().Contain(command =>
            command.Contains("catalogue_match_decisions", StringComparison.OrdinalIgnoreCase) &&
            command.Contains("source_revision", StringComparison.OrdinalIgnoreCase));
    }

    private sealed class CatalogueReadCapture : DbCommandInterceptor
    {
        public List<string> Commands { get; } = [];

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Commands.Add(command.CommandText);
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }
}
