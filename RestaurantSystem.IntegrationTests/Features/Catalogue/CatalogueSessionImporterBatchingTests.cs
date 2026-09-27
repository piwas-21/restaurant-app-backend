using System.Data.Common;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Catalogue.Dtos;
using RestaurantSystem.Api.Features.Catalogue.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Catalogue;

[Collection("Database Lane 3")]
public sealed class CatalogueSessionImporterBatchingTests(DatabaseFixture databaseFixture)
    : IntegrationTestBase(databaseFixture)
{
    private const string Actor = "catalogue-importer-batching-test";
    private static readonly JsonSerializerOptions WebOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Import_reuses_session_state_across_items_and_batches_skipped_statuses()
    {
        var session = NewSession();
        var templates = Enumerable.Range(0, 5).Select(index => NewTemplate(session.Id, session.RootTemplateId,
            index, isSelected: index < 3)).ToArray();
        session.Templates = templates;
        await using (var seed = DatabaseFixture.CreateContext())
        {
            seed.CatalogueImportSessions.Add(session);
            await seed.SaveChangesAsync();
        }

        var reads = new SessionReadCounter();
        await using var context = DatabaseFixture.CreateContext(reads);
        var central = new Mock<ICentralCatalogueClient>(MockBehavior.Strict);
        central.Setup(value => value.GetCurrentRevisionBatchAsync(
                It.IsAny<IReadOnlyList<CatalogueCurrentRevisionRequest>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<CatalogueCurrentRevisionRequest> requests, CancellationToken _) =>
                AvailableRevisions(requests));
        var failureId = templates[0].TemplateId;
        var executor = new Mock<ICatalogueTemplateImportExecutor>(MockBehavior.Strict);
        executor.Setup(value => value.ExecuteAsync(
                It.IsAny<CatalogueImportSession>(), It.IsAny<CatalogueImportSessionTemplate>(),
                It.IsAny<CatalogueImportBatchContext>(),
                It.IsAny<CancellationToken>()))
            .Returns((CatalogueImportSession _, CatalogueImportSessionTemplate item,
                CatalogueImportBatchContext _, CancellationToken _) =>
                item.TemplateId == failureId
                    ? Task.FromException<CatalogueTemplateImportOutcome>(new InvalidOperationException("fixture failure"))
                    : Task.FromResult(new CatalogueTemplateImportOutcome(CatalogueImportItemStatus.Imported,
                        "Category", Guid.NewGuid(), [])));
        var preview = new Mock<ICatalogueImportPreviewService>(MockBehavior.Strict);
        preview.Setup(value => value.PreviewAsync(session.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CatalogueImportPreviewDto(session.Id, 1,
                templates.Select(item => new CatalogueImportPreviewItemDto(
                    item.TemplateId, item.Revision, item.Type, item.TemplateId, item.IsSelected, "Create", null,
                    [], [], [])).ToArray()));
        var currentUser = new Mock<ICurrentUserService>();
        currentUser.Setup(value => value.GetAuditIdentifier()).Returns(Actor);
        var importLock = new Mock<ICatalogueImportLock>();
        importLock.Setup(value => value.AcquireAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NoopLease());
        var importer = new CatalogueSessionImporter(
            context,
            importLock.Object,
            new CatalogueImportStateStore(context, currentUser.Object),
            executor.Object,
            preview.Object,
            central.Object,
            NullLogger<CatalogueSessionImporter>.Instance);

        var result = await importer.ImportAsync(session.Id,
            new ImportCatalogueSessionRequest { ExpectedVersion = 1, IdempotencyKey = Guid.NewGuid().ToString("N") },
            CancellationToken.None);

        reads.SessionGraphReadCount.Should().Be(3,
            "the importer reads the graph for initial state and final result once, not once per template");
        result.Status.Should().Be(nameof(CatalogueImportStatus.PartiallyImported));
        result.Items.Should().HaveCount(5);
        result.Items.Single(item => item.TemplateId == failureId).Status.Should().Be(nameof(CatalogueImportItemStatus.Failed));
        result.Items.Where(item => item.Status == nameof(CatalogueImportItemStatus.Skipped)).Should().HaveCount(2);
        result.Items.Where(item => item.Status == nameof(CatalogueImportItemStatus.Imported)).Should().HaveCount(2);
        executor.Verify(value => value.ExecuteAsync(It.IsAny<CatalogueImportSession>(),
            It.IsAny<CatalogueImportSessionTemplate>(), It.IsAny<CatalogueImportBatchContext>(),
            It.IsAny<CancellationToken>()), Times.Exactly(3));
    }

    private static CatalogueImportSession NewSession()
    {
        var id = Guid.NewGuid();
        var templateId = $"import-batch-{id:N}";
        return new CatalogueImportSession
        {
            Id = id,
            RootTemplateId = templateId,
            RootRevision = 1,
            Locale = "en",
            IdempotencyKey = Guid.NewGuid().ToString("N"),
            AdoptionId = Guid.NewGuid(),
            CreateNewCopy = true,
            Version = 1,
            CreatedBy = Actor
        };
    }

    private static CatalogueImportSessionTemplate NewTemplate(Guid sessionId, string rootId, int index, bool isSelected)
    {
        var templateId = index == 0 ? rootId : $"{rootId}-{index}";
        var revision = Revision(templateId, 1);
        return new CatalogueImportSessionTemplate
        {
            Id = Guid.NewGuid(),
            SessionId = sessionId,
            TemplateId = templateId,
            Revision = 1,
            Type = "category",
            ContentHash = revision.ContentHash,
            RevisionJson = JsonSerializer.Serialize(revision, WebOptions),
            IsRoot = index == 0,
            IsSelectable = true,
            IsSelected = isSelected,
            Status = CatalogueImportItemStatus.Pending,
            CreatedBy = Actor
        };
    }

    private static CatalogueProxyResponse AvailableRevisions(
        IReadOnlyList<CatalogueCurrentRevisionRequest> requests) => new(
        StatusCodes.Status200OK,
        JsonSerializer.SerializeToElement(new
        {
            items = requests.Select(request => new
            {
                request.TemplateId,
                status = "available",
                revision = Revision(request.TemplateId, request.AdoptedRevision),
                adoptedRevisionWithdrawn = false
            }).ToArray()
        }, WebOptions));

    private static CentralCatalogueTemplateRevision Revision(string templateId, int revision) => new()
    {
        SchemaVersion = 1,
        TemplateId = templateId,
        Revision = revision,
        Type = "category",
        Name = $"Category {templateId}",
        SourceLocale = "en",
        Translations = new Dictionary<string, CentralCatalogueTranslation>(StringComparer.Ordinal),
        Provenance = JsonSerializer.SerializeToElement(new { source = "test" }),
        QualityStatus = "reviewed",
        CompatibleTenantContractVersions = [1],
        Payload = JsonSerializer.SerializeToElement(new { sortOrder = 0 }),
        ContentHash = new string('a', 64)
    };

    private sealed class SessionReadCounter : DbCommandInterceptor
    {
        private int _sessionGraphReadCount;

        public int SessionGraphReadCount => Volatile.Read(ref _sessionGraphReadCount);

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase) &&
                (command.CommandText.Contains("catalogue_import_sessions", StringComparison.OrdinalIgnoreCase) ||
                 command.CommandText.Contains("catalogue_import_session_templates", StringComparison.OrdinalIgnoreCase)))
            {
                Interlocked.Increment(ref _sessionGraphReadCount);
            }

            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }

    private sealed class NoopLease : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
