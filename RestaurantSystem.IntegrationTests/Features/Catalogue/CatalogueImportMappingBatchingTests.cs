using System.Data.Common;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Moq;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Catalogue.Dtos;
using RestaurantSystem.Api.Features.Catalogue.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Catalogue;

[Collection("Database Lane 3")]
public sealed class CatalogueImportMappingBatchingTests(DatabaseFixture databaseFixture)
    : IntegrationTestBase(databaseFixture)
{
    private const string Actor = "catalogue-import-mapping-batching-test";

    [Fact]
    public async Task Import_outcome_mappings_reuse_one_preflight_for_multiple_items_and_entries()
    {
        var adoptionId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var previousSessionId = Guid.NewGuid();
        var sourceId = $"batch-mappings-{Guid.NewGuid():N}";
        var firstTemplate = new CatalogueImportSessionTemplate
        {
            Id = Guid.NewGuid(),
            SessionId = sessionId,
            TemplateId = sourceId,
            Revision = 1,
            Type = "category",
            ContentHash = new string('a', 64),
            RevisionJson = RevisionJson(sourceId, 1),
            CreatedBy = Actor
        };
        var secondTemplate = new CatalogueImportSessionTemplate
        {
            Id = Guid.NewGuid(),
            SessionId = sessionId,
            TemplateId = $"{sourceId}-second",
            Revision = 2,
            Type = "category",
            ContentHash = new string('b', 64),
            RevisionJson = RevisionJson($"{sourceId}-second", 2),
            CreatedBy = Actor
        };
        var session = new CatalogueImportSession
        {
            Id = sessionId,
            RootTemplateId = sourceId,
            RootRevision = 1,
            Locale = "en",
            IdempotencyKey = Guid.NewGuid().ToString("N"),
            AdoptionId = adoptionId,
            Version = 1,
            CreatedBy = Actor,
            Templates = [firstTemplate, secondTemplate]
        };
        var previousSession = new CatalogueImportSession
        {
            Id = previousSessionId,
            RootTemplateId = sourceId,
            RootRevision = 1,
            Locale = "en",
            IdempotencyKey = Guid.NewGuid().ToString("N"),
            AdoptionId = Guid.NewGuid(),
            Version = 1,
            CreatedBy = Actor
        };
        var firstLocalId = Guid.NewGuid();
        await using (var seed = DatabaseFixture.CreateContext())
        {
            seed.CatalogueImportSessions.AddRange(session, previousSession);
            seed.CatalogueTemplateAdoptions.AddRange(
                Mapping(sessionId, adoptionId, sourceId, "entry-0", firstLocalId, isDefault: true),
                Mapping(previousSessionId, previousSession.AdoptionId, sourceId, "entry-1",
                    Guid.NewGuid(), isDefault: true));
            seed.CatalogueTemplateAdoptions.AddRange(Enumerable.Range(2, 24).Select(revision =>
                Mapping(sessionId, adoptionId, sourceId, null, Guid.NewGuid(), isDefault: true, revision)));
            await seed.SaveChangesAsync();
        }

        var entries = Enumerable.Range(0, 8).Select(index => new CatalogueTemplateEntryMapping(
            sourceId, 1, $"entry-{index}", "OptionSetEntry", index == 0 ? firstLocalId : Guid.NewGuid(),
            new string('a', 64))).ToArray();
        var counter = new ReadCommandCounter();
        await using var context = DatabaseFixture.CreateContext(counter);
        var currentUser = new Mock<ICurrentUserService>();
        currentUser.Setup(value => value.GetAuditIdentifier()).Returns(Actor);
        var stateStore = new CatalogueImportStateStore(context, currentUser.Object);
        var batchContext = await stateStore.LoadBatchContextAsync(session, CancellationToken.None);

        var added = await stateStore.AddOutcomeMappingsAsync(session, firstTemplate,
            new CatalogueTemplateImportOutcome(CatalogueImportItemStatus.Imported, null, null, entries),
            batchContext, CancellationToken.None);
        batchContext.RegisterCommitted(added);
        await stateStore.AddOutcomeMappingsAsync(session, secondTemplate,
            new CatalogueTemplateImportOutcome(CatalogueImportItemStatus.Imported, null, null,
                [new CatalogueTemplateEntryMapping(secondTemplate.TemplateId, secondTemplate.Revision,
                    "entry-second", "OptionSetEntry", Guid.NewGuid(), new string('b', 64))]),
            batchContext, CancellationToken.None);

        counter.ReadCount.Should().Be(1,
            "the import batch preloads session and default mappings once for all selected items and entries");
        counter.Commands.Should().ContainSingle();
        counter.Commands.Should().OnlyContain(command =>
            command.Contains("source_template_id", StringComparison.OrdinalIgnoreCase) &&
            command.Contains("source_revision", StringComparison.OrdinalIgnoreCase));
        context.CatalogueTemplateAdoptions.Local.Should().HaveCount(8);
        context.CatalogueTemplateAdoptions.Local.Single(value => value.SourceEntryId == "entry-1")
            .IsDefault.Should().BeFalse();
        context.CatalogueTemplateAdoptions.Local.Where(value => value.SourceEntryId is not "entry-0" and not "entry-1")
            .Should().OnlyContain(value => value.IsDefault);
    }

    [Fact]
    public async Task Resolver_preflight_covers_multi_item_dependency_and_mixed_revision_reuse_without_more_reads()
    {
        var session = Session();
        var categoryIds = Enumerable.Range(0, 3).Select(_ => Guid.NewGuid()).ToArray();
        var child = Template(session.Id, "new-child", 1, "category", CatalogueImportItemStatus.Pending);
        var parent = Template(session.Id, "parent-item", 1, "item", CatalogueImportItemStatus.Pending);
        var oldRevision = Template(session.Id, "mixed-source", 1, "category",
            CatalogueImportItemStatus.Imported, categoryIds[0]);
        var currentRevision = Template(session.Id, "mixed-source", 2, "category",
            CatalogueImportItemStatus.Imported, categoryIds[2]);
        parent.RevisionJson = RevisionJson(parent.TemplateId, parent.Revision, "item",
            [new CentralCatalogueDependency { TemplateId = child.TemplateId, Revision = child.Revision, Role = "category" }]);
        session.Templates = [child, parent, oldRevision, currentRevision];

        await using (var seed = DatabaseFixture.CreateContext())
        {
            seed.Categories.AddRange(categoryIds.Select((id, index) => new Category
            {
                Id = id,
                Name = $"Batch category {index} {Guid.NewGuid():N}",
                CreatedBy = Actor,
                CreatedAt = DateTime.UtcNow
            }));
            seed.CatalogueImportSessions.Add(session);
            seed.CatalogueTemplateAdoptions.AddRange(
                Mapping(session.Id, Guid.NewGuid(), "mixed-source", null, categoryIds[0], true, 1, "Category"),
                Mapping(session.Id, session.AdoptionId, "mixed-source", null, categoryIds[1], false, 2, "Category"),
                Mapping(session.Id, Guid.NewGuid(), "mixed-source", null, categoryIds[2], true, 2, "Category"));
            await seed.SaveChangesAsync();
        }

        var counter = new ReadCommandCounter();
        await using var context = DatabaseFixture.CreateContext(counter);
        var currentUser = new Mock<ICurrentUserService>();
        currentUser.Setup(value => value.GetAuditIdentifier()).Returns(Actor);
        var stateStore = new CatalogueImportStateStore(context, currentUser.Object);
        session = await stateStore.LoadAsync(session.Id, CancellationToken.None);
        counter.Reset();

        var batchContext = await stateStore.LoadBatchContextAsync(session, CancellationToken.None);
        counter.ReadCount.Should().Be(2,
            "all pinned adoption mappings and local category existence are loaded in one mapping read and one type-batched existence read");
        var resolver = new CatalogueImportEntityResolver(batchContext);
        resolver.FindReusableMapping("mixed-source", 1, "Category")!.LocalEntityId.Should().Be(categoryIds[0]);
        resolver.FindReusableMapping("mixed-source", 2, "Category")!.LocalEntityId.Should().Be(categoryIds[1],
            "the current adoption mapping wins over a default for the same source revision");

        var childCategoryId = Guid.NewGuid();
        context.Categories.Add(new Category
        {
            Id = childCategoryId,
            Name = $"New child {Guid.NewGuid():N}",
            CreatedBy = Actor,
            CreatedAt = DateTime.UtcNow
        });
        var childEntity = session.Templates.Single(item => item.TemplateId == child.TemplateId);
        var added = await stateStore.AddOutcomeMappingsAsync(session, childEntity,
            new CatalogueTemplateImportOutcome(CatalogueImportItemStatus.Imported, "Category", childCategoryId, []),
            batchContext, CancellationToken.None);
        childEntity.Status = CatalogueImportItemStatus.Imported;
        childEntity.LocalEntityType = "Category";
        childEntity.LocalEntityId = childCategoryId;
        await context.SaveChangesAsync();
        batchContext.RegisterCommitted(added);

        var dependency = CatalogueSessionMapper.ParseRevision(parent.RevisionJson).Dependencies.Single();
        resolver.ResolveDependency(session,
            new CatalogueSourceReference(dependency.TemplateId, dependency.Revision), "Category")
            .Should().Be(childCategoryId,
                "the second item resolves its first item's newly committed mapping from the session batch state");
        resolver.FindReusableMapping(child.TemplateId, child.Revision, "Category")!.LocalEntityId
            .Should().Be(childCategoryId);
        resolver.FindReusableMapping("mixed-source", 1, "Category")!.LocalEntityId.Should().Be(categoryIds[0]);
        counter.ReadCount.Should().Be(2, "resolver work across all graph items is in-memory after preflight");

        var lateMapping = Template(session.Id, "late-source", 1, "category", CatalogueImportItemStatus.Pending);
        session.Templates.Add(lateMapping);
        await using (var concurrent = DatabaseFixture.CreateContext())
        {
            concurrent.CatalogueTemplateAdoptions.Add(Mapping(
                session.Id, Guid.NewGuid(), lateMapping.TemplateId, null, categoryIds[0], true, 1, "Category"));
            await concurrent.SaveChangesAsync();
        }

        await batchContext.RefreshAsync(context, session, CancellationToken.None);
        resolver.FindReusableMapping(lateMapping.TemplateId, lateMapping.Revision, "Category")!.LocalEntityId
            .Should().Be(categoryIds[0], "a refreshed batch sees a concurrently committed default mapping after a uniqueness race");
        counter.ReadCount.Should().Be(4, "concurrency recovery refreshes the whole batch once, not each mapping");
    }

    private static CatalogueTemplateAdoption Mapping(
        Guid sessionId,
        Guid adoptionId,
        string templateId,
        string? sourceEntryId,
        Guid localId,
        bool isDefault,
        int revision = 1,
        string entityType = "OptionSetEntry") => new()
        {
            Id = Guid.NewGuid(),
            SessionId = sessionId,
            AdoptionId = adoptionId,
            SourceTemplateId = templateId,
            SourceRevision = revision,
            SourceEntryId = sourceEntryId,
            LocalEntityType = entityType,
            LocalEntityId = localId,
            ContentHash = new string('a', 64),
            IsDefault = isDefault,
            CreatedBy = Actor
        };

    private static string RevisionJson(string templateId, int revision) => JsonSerializer.Serialize(
        Revision(templateId, revision, "category"), new JsonSerializerOptions(JsonSerializerDefaults.Web));

    private static string RevisionJson(
        string templateId,
        int revision,
        string type,
        IReadOnlyList<CentralCatalogueDependency> dependencies) => JsonSerializer.Serialize(
        Revision(templateId, revision, type) with { Dependencies = dependencies.ToList() },
        new JsonSerializerOptions(JsonSerializerDefaults.Web));

    private static CentralCatalogueTemplateRevision Revision(string templateId, int revision, string type) => new()
    {
        SchemaVersion = 1,
        TemplateId = templateId,
        Revision = revision,
        Type = type,
        Name = templateId,
        SourceLocale = "en",
        Translations = [],
        Provenance = JsonSerializer.SerializeToElement(new { source = "test" }),
        QualityStatus = "reviewed",
        CompatibleTenantContractVersions = [1],
        Payload = JsonSerializer.SerializeToElement(new { sortOrder = 0 }),
        Dependencies = [],
        ContentHash = new string('a', 64)
    };

    private static CatalogueImportSession Session() => new()
    {
        Id = Guid.NewGuid(),
        RootTemplateId = $"root-{Guid.NewGuid():N}",
        RootRevision = 1,
        Locale = "en",
        IdempotencyKey = Guid.NewGuid().ToString("N"),
        AdoptionId = Guid.NewGuid(),
        CreateNewCopy = false,
        Version = 1,
        CreatedBy = Actor
    };

    private static CatalogueImportSessionTemplate Template(
        Guid sessionId,
        string templateId,
        int revision,
        string type,
        CatalogueImportItemStatus status,
        Guid? localEntityId = null) => new()
        {
            Id = Guid.NewGuid(),
            SessionId = sessionId,
            TemplateId = templateId,
            Revision = revision,
            Type = type,
            ContentHash = new string('a', 64),
            RevisionJson = RevisionJson(templateId, revision),
            IsSelected = true,
            IsSelectable = true,
            Status = status,
            LocalEntityType = localEntityId.HasValue ? "Category" : null,
            LocalEntityId = localEntityId,
            CreatedBy = Actor
        };

    private sealed class ReadCommandCounter : DbCommandInterceptor
    {
        private int _readCount;
        private readonly List<string> _commands = [];

        public int ReadCount => Volatile.Read(ref _readCount);
        public IReadOnlyList<string> Commands => _commands;

        public void Reset()
        {
            Interlocked.Exchange(ref _readCount, 0);
            _commands.Clear();
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase))
            {
                Interlocked.Increment(ref _readCount);
                _commands.Add(command.CommandText);
            }

            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }
}
