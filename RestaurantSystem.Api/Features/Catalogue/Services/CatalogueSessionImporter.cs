using Microsoft.EntityFrameworkCore;
using Npgsql;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.Catalogue.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Catalogue.Services;

public sealed class CatalogueSessionImporter(
    ApplicationDbContext context,
    ICatalogueImportLock importLock,
    ICatalogueImportStateStore stateStore,
    ICatalogueTemplateImportExecutor executor,
    ICatalogueImportPreviewService preview,
    ICentralCatalogueClient catalogue,
    ILogger<CatalogueSessionImporter> logger) : ICatalogueSessionImporter
{
    private readonly CataloguePublishedRevisionLoader revisionLoader = new(catalogue);

    public async Task<CatalogueImportResultDto> ImportAsync(
        Guid sessionId,
        ImportCatalogueSessionRequest request,
        CancellationToken cancellationToken)
    {
        var key = ValidateRequest(request);
        var rootTemplateId = await context.CatalogueImportSessions.AsNoTracking()
            .Where(session => session.Id == sessionId)
            .Select(session => session.RootTemplateId)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("Catalogue import session was not found");

        await using var rootLease = await importLock.AcquireAsync($"root:{rootTemplateId}", cancellationToken);
        context.ChangeTracker.Clear();
        var session = await stateStore.LoadAsync(sessionId, cancellationToken);

        if (session.Status == CatalogueImportStatus.Imported)
        {
            return stateStore.ReadCachedResult(session);
        }

        if (CatalogueImportIdempotencyRules.CanReplayResult(session, key, request.ExpectedVersion))
        {
            return stateStore.ReadCachedResult(session);
        }

        if (!CatalogueImportIdempotencyRules.CanStartAttempt(session, key, request.ExpectedVersion))
        {
            throw new ConflictException("Import session changed or its idempotency key cannot be resumed. Reload it before importing.");
        }

        await EnsureSelectedPinsPublishedAsync(session, cancellationToken);
        await ReuseDefaultAdoptionAsync(session, cancellationToken);
        await EnsurePreviewReadyAsync(sessionId, cancellationToken);
        var batchContext = await stateStore.LoadBatchContextAsync(session, cancellationToken);
        session.LastImportIdempotencyKey = key;
        session.LastImportExpectedVersion = request.ExpectedVersion;
        session.Status = CatalogueImportStatus.Importing;
        session.CompletedAt = null;
        session.LastImportResultJson = null;
        session.Version++;
        session.UpdatedAt = DateTime.UtcNow;
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConflictException("Import session changed. Reload it before importing.");
        }

        return await ImportSelectedTemplatesAsync(session, batchContext, cancellationToken);
    }

    private async Task<CatalogueImportResultDto> ImportSelectedTemplatesAsync(
        CatalogueImportSession session,
        CatalogueImportBatchContext batchContext,
        CancellationToken cancellationToken)
    {
        var selectedOrder = CatalogueImportOrder.SelectedDependenciesFirst(session.Templates.ToArray());
        var byKey = session.Templates.ToDictionary(item => (item.TemplateId, item.Revision));
        var statuses = session.Templates.ToDictionary(
            item => (item.TemplateId, item.Revision), item => item.Status);

        var skippedItems = session.Templates.Where(item => !item.IsSelected &&
            item.Status is CatalogueImportItemStatus.Pending or CatalogueImportItemStatus.Failed).ToArray();
        await stateStore.SaveSkippedAsync(session, skippedItems, cancellationToken);
        foreach (var item in skippedItems)
        {
            statuses[(item.TemplateId, item.Revision)] = CatalogueImportItemStatus.Skipped;
        }

        foreach (var template in selectedOrder)
        {
            var key = (template.TemplateId, template.Revision);
            if (statuses[key] is CatalogueImportItemStatus.Imported or CatalogueImportItemStatus.Skipped)
            {
                continue;
            }

            if (HasFailedPrerequisite(template, byKey, statuses))
            {
                await stateStore.SaveFailedAsync(session, template, "DEPENDENCY_IMPORT_FAILED", cancellationToken);
                statuses[key] = CatalogueImportItemStatus.Failed;
                continue;
            }

            statuses[key] = await ImportTemplateAsync(session, template, batchContext, cancellationToken);
        }

        return await stateStore.CompleteAsync(session.Id, cancellationToken);
    }

    private static bool HasFailedPrerequisite(
        CatalogueImportSessionTemplate template,
        Dictionary<(string TemplateId, int Revision), CatalogueImportSessionTemplate> byKey,
        Dictionary<(string TemplateId, int Revision), CatalogueImportItemStatus> statuses) =>
        CatalogueImportOrder.SelectedPrerequisites(template)
            .Where(byKey.ContainsKey)
            .Any(dependency => byKey[dependency].IsSelected &&
                statuses[dependency] == CatalogueImportItemStatus.Failed);

    private async Task EnsurePreviewReadyAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        var result = await preview.PreviewAsync(sessionId, cancellationToken);
        if (result.Items.Any(item => item.IsSelected && item.BlockingIssues.Count > 0))
        {
            throw new BadRequestException("Resolve the blocking import review issues before importing.");
        }
    }

    private async Task EnsureSelectedPinsPublishedAsync(
        CatalogueImportSession session,
        CancellationToken cancellationToken)
    {
        var requests = session.Templates.Where(item => item.IsSelected)
            .Select(item => new CatalogueCurrentRevisionRequest(item.TemplateId, item.Revision))
            .ToArray();
        if (requests.Length == 0)
        {
            return;
        }

        var results = await revisionLoader.LoadBatchAsync(requests, cancellationToken);
        foreach (var request in requests)
        {
            if (!results.TryGetValue(request.TemplateId, out var result) || result.Status == "Unavailable")
            {
                throw new ServiceUnavailableException(
                    "Catalogue status could not be verified. Retry the import after the catalogue is available.");
            }

            if (result.Status != "Available" || result.Revision is null || result.Metadata is null ||
                result.Metadata.AdoptedRevisionWithdrawn is not false ||
                result.Revision.Revision < request.AdoptedRevision)
            {
                throw new ConflictException(
                    $"The selected source revision {request.TemplateId}@{request.AdoptedRevision} is no longer verified for adoption.");
            }
        }
    }

    private async Task<CatalogueImportItemStatus> ImportTemplateAsync(
        CatalogueImportSession session,
        CatalogueImportSessionTemplate item,
        CatalogueImportBatchContext batchContext,
        CancellationToken cancellationToken)
    {
        var priorSessionVersion = session.Version;
        var priorSessionUpdatedAt = session.UpdatedAt;
        var priorStatus = item.Status;
        var priorFailureCode = item.FailureCode;
        var priorLocalEntityType = item.LocalEntityType;
        var priorLocalEntityId = item.LocalEntityId;
        var priorItemUpdatedAt = item.UpdatedAt;
        Exception failure;
        await using (var transaction = await context.Database.BeginTransactionAsync(cancellationToken))
        {
            try
            {
                var outcome = await executor.ExecuteAsync(session, item, batchContext, cancellationToken);
                if (outcome.Status == CatalogueImportItemStatus.Failed)
                {
                    throw new BadRequestException("Catalogue item import was rejected");
                }

                var committedMappings = await stateStore.AddOutcomeMappingsAsync(
                    session, item, outcome, batchContext, cancellationToken);
                item.LocalEntityType = outcome.LocalEntityType;
                item.LocalEntityId = outcome.LocalEntityId;
                item.Status = outcome.Status;
                item.FailureCode = null;
                item.UpdatedAt = DateTime.UtcNow;
                session.Version++;
                session.UpdatedAt = DateTime.UtcNow;
                await context.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                batchContext.RegisterCommitted(committedMappings);
                if (outcome.LocalEntityType is not null && outcome.LocalEntityId is Guid localEntityId)
                {
                    batchContext.RegisterLocalEntity(outcome.LocalEntityType, localEntityId);
                }
                return outcome.Status;
            }
            catch (OperationCanceledException)
            {
                await transaction.RollbackAsync(CancellationToken.None);
                throw;
            }
            catch (Exception exception)
            {
                await transaction.RollbackAsync(CancellationToken.None);
                failure = exception;
            }
        }

        var itemFailure = failure;
        context.ChangeTracker.Clear();
        session.Version = priorSessionVersion;
        session.UpdatedAt = priorSessionUpdatedAt;
        item.Status = priorStatus;
        item.FailureCode = priorFailureCode;
        item.LocalEntityType = priorLocalEntityType;
        item.LocalEntityId = priorLocalEntityId;
        item.UpdatedAt = priorItemUpdatedAt;
        if (IsCatalogueMappingUniqueViolation(itemFailure))
        {
            try
            {
                await batchContext.RefreshAsync(context, session, cancellationToken);
            }
            catch (Exception refreshException) when (refreshException is not OperationCanceledException)
            {
                batchContext.MarkStale();
                logger.LogWarning(refreshException, "Catalogue adoption state refresh failed with {FailureType}",
                    refreshException.GetType().Name);
            }
        }

        logger.LogWarning(itemFailure, "Catalogue import item {TemplateId}@{Revision} failed with {FailureType}",
            item.TemplateId, item.Revision, itemFailure.GetType().Name);
        await stateStore.SaveFailedAsync(session, item, FailureCode(itemFailure), cancellationToken);
        return CatalogueImportItemStatus.Failed;
    }

    private async Task ReuseDefaultAdoptionAsync(
        CatalogueImportSession session,
        CancellationToken cancellationToken)
    {
        if (session.CreateNewCopy)
        {
            return;
        }

        var existingAdoptionId = await context.CatalogueTemplateAdoptions.AsNoTracking()
            .Where(mapping => mapping.SourceTemplateId == session.RootTemplateId &&
                mapping.SourceEntryId == null && mapping.IsDefault)
            .OrderByDescending(mapping => mapping.SourceRevision)
            .Select(mapping => (Guid?)mapping.AdoptionId)
            .FirstOrDefaultAsync(cancellationToken);
        if (existingAdoptionId is null || existingAdoptionId == session.AdoptionId)
        {
            return;
        }

        session.AdoptionId = existingAdoptionId.Value;
        session.UpdatedAt = DateTime.UtcNow;
        await context.SaveChangesAsync(cancellationToken);
    }

    private static string ValidateRequest(ImportCatalogueSessionRequest request)
    {
        var key = request.IdempotencyKey?.Trim() ?? string.Empty;
        if (request.ExpectedVersion < 1 || key.Length is < 1 or > 128)
        {
            throw new BadRequestException("A current session version and import idempotency key are required.");
        }

        return key;
    }

    private static string FailureCode(Exception exception) => exception switch
    {
        ConflictException => "IMPORT_CONFLICT",
        NotFoundException => "LOCAL_RECORD_MISSING",
        BadRequestException { ErrorCode: "REUSE_KIND_MISMATCH" } => "REUSE_KIND_MISMATCH",
        BadRequestException => "IMPORT_VALIDATION_FAILED",
        DbUpdateException => "IMPORT_PERSISTENCE_FAILED",
        _ => "IMPORT_FAILED"
    };

    private static bool IsCatalogueMappingUniqueViolation(Exception exception) =>
        exception is DbUpdateException { InnerException: PostgresException postgres } &&
        postgres.SqlState == PostgresErrorCodes.UniqueViolation &&
        postgres.ConstraintName?.StartsWith("IX_catalogue_template_adoptions_", StringComparison.OrdinalIgnoreCase) == true;
}
