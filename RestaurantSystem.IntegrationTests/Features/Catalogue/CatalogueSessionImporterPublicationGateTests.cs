using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
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
public sealed class CatalogueSessionImporterPublicationGateTests(DatabaseFixture databaseFixture)
    : IntegrationTestBase(databaseFixture)
{
    [Fact]
    public async Task Import_rejects_a_revision_withdrawn_since_draft_without_local_writes()
    {
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var session = await CreateDraftSessionAsync(context);
        var central = new Mock<ICentralCatalogueClient>();
        central.Setup(value => value.GetCurrentRevisionBatchAsync(
                It.IsAny<IReadOnlyList<CatalogueCurrentRevisionRequest>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response(StatusCodes.Status200OK, new
            {
                items = new[]
                {
                    new
                    {
                        templateId = session.RootTemplateId,
                        status = "withdrawn",
                        revision = (object?)null,
                        adoptedRevisionWithdrawn = true
                    }
                }
            }));

        var act = () => CreateImporter(context, central.Object,
            scope.ServiceProvider.GetRequiredService<ICurrentUserService>()).ImportAsync(
            session.Id, Request(), CancellationToken.None);

        await act.Should().ThrowAsync<ConflictException>();
        await AssertDraftUnchangedAsync(context, session.Id, session.RootTemplateId);
        central.Verify(value => value.GetCurrentRevisionBatchAsync(
            It.Is<IReadOnlyList<CatalogueCurrentRevisionRequest>>(items =>
                items.Count == 1 && items[0].TemplateId == session.RootTemplateId && items[0].AdoptedRevision == 1),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Import_fails_closed_on_unverified_adopted_revision_status_without_local_writes()
    {
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var session = await CreateDraftSessionAsync(context);
        var central = new Mock<ICentralCatalogueClient>();
        central.Setup(value => value.GetCurrentRevisionBatchAsync(
                It.IsAny<IReadOnlyList<CatalogueCurrentRevisionRequest>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(AvailableResponse(session.RootTemplateId, adoptedRevisionWithdrawn: null));

        var act = () => CreateImporter(context, central.Object,
            scope.ServiceProvider.GetRequiredService<ICurrentUserService>()).ImportAsync(
            session.Id, Request(), CancellationToken.None);

        await act.Should().ThrowAsync<ConflictException>();
        await AssertDraftUnchangedAsync(context, session.Id, session.RootTemplateId);
    }

    [Fact]
    public async Task Import_outage_leaves_session_retryable_and_does_not_write_import_state()
    {
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var session = await CreateDraftSessionAsync(context);
        var central = new Mock<ICentralCatalogueClient>();
        central.Setup(value => value.GetCurrentRevisionBatchAsync(
                It.IsAny<IReadOnlyList<CatalogueCurrentRevisionRequest>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response(StatusCodes.Status503ServiceUnavailable, new { error = "catalogue_unavailable" }));

        var act = () => CreateImporter(context, central.Object,
            scope.ServiceProvider.GetRequiredService<ICurrentUserService>()).ImportAsync(
            session.Id, Request(), CancellationToken.None);

        await act.Should().ThrowAsync<ServiceUnavailableException>();
        await AssertDraftUnchangedAsync(context, session.Id, session.RootTemplateId);
    }

    private static async Task<CatalogueImportSession> CreateDraftSessionAsync(ApplicationDbContext context)
    {
        var sessionId = Guid.NewGuid();
        var templateId = $"publication-check-{sessionId:N}";
        var template = new CatalogueImportSessionTemplate
        {
            Id = Guid.NewGuid(),
            SessionId = sessionId,
            TemplateId = templateId,
            Revision = 1,
            Type = "category",
            ContentHash = new string('a', 64),
            RevisionJson = "{}",
            IsRoot = true,
            IsSelectable = true,
            IsSelected = true,
            Status = CatalogueImportItemStatus.Pending,
            CreatedBy = "test"
        };
        var session = new CatalogueImportSession
        {
            Id = sessionId,
            RootTemplateId = templateId,
            RootRevision = 1,
            Locale = "en",
            IdempotencyKey = $"publication-check-{sessionId:N}",
            AdoptionId = Guid.NewGuid(),
            Version = 1,
            CreatedBy = "test",
            Templates = [template]
        };
        context.CatalogueImportSessions.Add(session);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        return session;
    }

    private CatalogueSessionImporter CreateImporter(
        ApplicationDbContext context,
        ICentralCatalogueClient central,
        ICurrentUserService currentUser)
    {
        return new CatalogueSessionImporter(
            context,
            new CatalogueImportLock(context),
            new CatalogueImportStateStore(context, currentUser),
            Mock.Of<ICatalogueTemplateImportExecutor>(),
            Mock.Of<ICatalogueImportPreviewService>(),
            central,
            NullLogger<CatalogueSessionImporter>.Instance);
    }

    private static ImportCatalogueSessionRequest Request() => new()
    {
        ExpectedVersion = 1,
        IdempotencyKey = Guid.NewGuid().ToString("N")
    };

    private static async Task AssertDraftUnchangedAsync(
        ApplicationDbContext context,
        Guid sessionId,
        string templateId)
    {
        context.ChangeTracker.Clear();
        var session = await context.CatalogueImportSessions.AsNoTracking()
            .Include(value => value.Templates)
            .SingleAsync(value => value.Id == sessionId);
        session.Status.Should().Be(CatalogueImportStatus.Draft);
        session.Version.Should().Be(1);
        session.LastImportIdempotencyKey.Should().BeNull();
        session.Templates.Should().ContainSingle(item => item.TemplateId == templateId &&
            item.Status == CatalogueImportItemStatus.Pending && item.IsSelected);
        (await context.CatalogueTemplateAdoptions.AsNoTracking()
            .CountAsync(value => value.AdoptionId == session.AdoptionId)).Should().Be(0);
    }

    private static CatalogueProxyResponse AvailableResponse(string templateId, bool? adoptedRevisionWithdrawn) =>
        Response(StatusCodes.Status200OK, new
        {
            items = new[]
            {
                new
                {
                    templateId,
                    status = "available",
                    revision = new
                    {
                        schemaVersion = 1,
                        templateId,
                        revision = 2,
                        type = "category",
                        name = "Reviewed category",
                        sourceLocale = "en",
                        translations = new { },
                        localeFallbacks = Array.Empty<string>(),
                        dependencies = Array.Empty<object>(),
                        provenance = new { source = "test" },
                        qualityStatus = "reviewed",
                        compatibleTenantContractVersions = new[] { 1 },
                        payload = new { sortOrder = 0 },
                        contentHash = new string('b', 64)
                    },
                    adoptedRevisionWithdrawn
                }
            }
        });

    private static CatalogueProxyResponse Response(int statusCode, object body)
    {
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(body,
            new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        return new CatalogueProxyResponse(statusCode, document.RootElement.Clone());
    }
}
