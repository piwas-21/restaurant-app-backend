using System.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Catalogue.Dtos;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Catalogue.Services;

public sealed class CatalogueRevisionChangeService(
    ApplicationDbContext context,
    ICentralCatalogueClient catalogue,
    ICurrentUserService currentUser) : ICatalogueRevisionChangeService
{
    private const int MaximumFieldPaths = 128;
    private const int MaximumFieldPathLength = 256;
    private const int MaximumContentHashLength = 128;
    private readonly CataloguePublishedRevisionLoader revisionLoader = new(catalogue);
    private readonly CatalogueRevisionChangesReader changesReader = new(context, catalogue);
    private readonly CatalogueRevisionLocalTextReader localTextReader = new(context);
    private readonly CatalogueRevisionLocalTextStore localTextWriter = new(context, currentUser);

    public Task<CatalogueRevisionChangesDto> GetAsync(Guid sessionId, CancellationToken cancellationToken) =>
        changesReader.GetAsync(sessionId, cancellationToken);

    public async Task<CatalogueRevisionFieldApplyResultDto> ApplyFieldsAsync(
        Guid sessionId,
        ApplyCatalogueRevisionFieldsRequest request,
        CancellationToken cancellationToken)
    {
        ValidateRequest(request);
        var loadedByTemplate = await revisionLoader.LoadBatchAsync(
            [new CatalogueCurrentRevisionRequest(request.TemplateId, request.AdoptedRevision)], cancellationToken);
        var loaded = loadedByTemplate.GetValueOrDefault(request.TemplateId);
        if (loaded is null || loaded.Status != "Available" || loaded.Metadata is null ||
            loaded.Metadata.Revision != request.CurrentRevision ||
            loaded.Metadata.ContentHash != request.CurrentContentHash || loaded.Revision is null)
        {
            throw loaded?.Status == "Unavailable"
                ? new ServiceUnavailableException(loaded.Notice ?? "Catalogue is temporarily unavailable.")
                : new ConflictException(loaded?.Notice ?? "Catalogue revision changed. Reload revision changes.");
        }

        var revision = loaded.Revision;
        await using var transaction = await context.Database.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);
        try
        {
            var session = await context.CatalogueImportSessions.FirstOrDefaultAsync(
                value => value.Id == sessionId, cancellationToken)
                ?? throw new NotFoundException("Catalogue import session was not found");
            if (session.Version != request.ExpectedSessionVersion)
            {
                throw new ConflictException("Import session changed. Reload it before applying revision fields.");
            }

            var adoption = await context.CatalogueTemplateAdoptions.FirstOrDefaultAsync(value =>
                value.AdoptionId == session.AdoptionId && value.SourceTemplateId == request.TemplateId &&
                value.SourceRevision == request.AdoptedRevision && value.SourceEntryId == null, cancellationToken)
                ?? throw new ConflictException("The adopted tenant mapping changed. Reload revision changes.");
            if (request.CurrentRevision < adoption.SourceRevision)
            {
                throw new ConflictException("A revision update cannot move an adoption backwards.");
            }

            var type = CatalogueRevisionTemplateTypes.ForEntity(adoption.LocalEntityType);
            if (type is null || type != revision.Type)
            {
                throw new ConflictException("The source template type no longer matches its tenant mapping.");
            }

            var mappedEntries = await context.CatalogueTemplateAdoptions.Where(value =>
                    value.AdoptionId == session.AdoptionId && value.SourceTemplateId == request.TemplateId &&
                    value.SourceEntryId != null)
                .OrderBy(value => value.SourceRevision)
                .ThenBy(value => value.CreatedAt)
                .ToListAsync(cancellationToken);
            var entryMappings = mappedEntries
                .GroupBy(value => (value.SourceEntryId, value.LocalEntityType))
                .Select(group => group.Last())
                .ToArray();
            var baseline = CatalogueRevisionBaseline.Read(adoption.BaselineFieldsJson, type,
                adoption.SourceRevision, adoption.ContentHash);
            var sourceFields = CatalogueRevisionBaseline.Fields(revision, type);
            var changedPaths = sourceFields.Keys.Union(baseline.Keys, StringComparer.Ordinal)
                .Where(path => !baseline.TryGetValue(path, out var saved) ||
                    !sourceFields.TryGetValue(path, out var latest) ||
                    !string.Equals(saved.Value, latest, StringComparison.Ordinal))
                .ToHashSet(StringComparer.Ordinal);
            if (request.FieldPaths.Any(path => !changedPaths.Contains(path)))
            {
                throw new ConflictException("A selected field is no longer changed in the current revision.");
            }

            if (request.FieldPaths.Any(path => path.StartsWith("sections[", StringComparison.Ordinal) &&
                    !sourceFields.ContainsKey(path)))
            {
                throw new ConflictException("Removed bundle sections cannot be changed through text-only revision updates.");
            }

            var localFields = await localTextReader.ReadAsync(adoption, type, entryMappings, cancellationToken);
            if (localFields.Count == 0)
            {
                throw new ConflictException("The mapped tenant record no longer exists.");
            }
            var localHash = CatalogueRevisionBaseline.ComputeLocalHash(localFields);
            if (!string.Equals(localHash, request.ExpectedLocalHash, StringComparison.Ordinal))
            {
                throw new ConflictException("Tenant text changed after the revision preview. Reload before applying.");
            }

            await localTextWriter.ApplyAsync(adoption, type, revision, entryMappings,
                request.FieldPaths, cancellationToken);
            var updated = await AdvanceAdoptionBaselineAsync(
                session, adoption, entryMappings, revision, request.FieldPaths, cancellationToken);
            session.Version++;
            session.UpdatedAt = DateTime.UtcNow;
            session.UpdatedBy = currentUser.GetAuditIdentifier();
            await context.SaveChangesAsync(cancellationToken);
            var updatedFields = await localTextReader.ReadAsync(updated, type, entryMappings, cancellationToken);
            var result = new CatalogueRevisionFieldApplyResultDto(
                session.Id,
                session.Version,
                request.TemplateId,
                revision.Revision,
                revision.ContentHash,
                request.FieldPaths,
                CatalogueRevisionBaseline.ComputeLocalHash(updatedFields));
            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConflictException("Import session or tenant mapping changed. Reload before applying revision fields.");
        }
        catch (DbUpdateException exception) when (IsRevisionWriteConflict(exception))
        {
            throw new ConflictException("Import session or tenant mapping changed. Reload before applying revision fields.");
        }
        catch (PostgresException exception) when (IsRevisionWriteConflict(exception))
        {
            throw new ConflictException("Import session or tenant mapping changed. Reload before applying revision fields.");
        }
    }

    private async Task<CatalogueTemplateAdoption> AdvanceAdoptionBaselineAsync(
        CatalogueImportSession session,
        CatalogueTemplateAdoption adoption,
        IReadOnlyCollection<CatalogueTemplateAdoption> entryMappings,
        CentralCatalogueTemplateRevision revision,
        IReadOnlyCollection<string> selectedPaths,
        CancellationToken cancellationToken)
    {
        var mapping = await context.CatalogueTemplateAdoptions.FirstOrDefaultAsync(value =>
            value.AdoptionId == session.AdoptionId && value.SourceTemplateId == revision.TemplateId &&
            value.SourceRevision == revision.Revision && value.SourceEntryId == null &&
            value.LocalEntityType == adoption.LocalEntityType, cancellationToken);
        var priorJson = mapping?.BaselineFieldsJson ?? adoption.BaselineFieldsJson;
        var nextBaseline = CatalogueRevisionBaseline.Advance(priorJson, revision, revision.Type,
            selectedPaths, adoption.SourceRevision, adoption.ContentHash);
        if (mapping is null)
        {
            var hasDefault = adoption.IsDefault && await context.CatalogueTemplateAdoptions.AnyAsync(value =>
                value.SourceTemplateId == revision.TemplateId && value.SourceRevision == revision.Revision &&
                value.SourceEntryId == null && value.LocalEntityType == adoption.LocalEntityType && value.IsDefault,
                cancellationToken);
            mapping = new CatalogueTemplateAdoption
            {
                Id = Guid.NewGuid(),
                AdoptionId = session.AdoptionId,
                SessionId = session.Id,
                SourceTemplateId = revision.TemplateId,
                SourceRevision = revision.Revision,
                LocalEntityType = adoption.LocalEntityType,
                LocalEntityId = adoption.LocalEntityId,
                ContentHash = revision.ContentHash,
                BaselineFieldsJson = nextBaseline,
                IsDefault = adoption.IsDefault && !hasDefault,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = currentUser.GetAuditIdentifier()
            };
            context.CatalogueTemplateAdoptions.Add(mapping);
            foreach (var entry in entryMappings)
            {
                context.CatalogueTemplateAdoptions.Add(new CatalogueTemplateAdoption
                {
                    Id = Guid.NewGuid(),
                    AdoptionId = session.AdoptionId,
                    SessionId = session.Id,
                    SourceTemplateId = revision.TemplateId,
                    SourceRevision = revision.Revision,
                    SourceEntryId = entry.SourceEntryId,
                    LocalEntityType = entry.LocalEntityType,
                    LocalEntityId = entry.LocalEntityId,
                    ContentHash = revision.ContentHash,
                    IsDefault = false,
                    CreatedAt = DateTime.UtcNow,
                    CreatedBy = currentUser.GetAuditIdentifier()
                });
            }
        }
        else
        {
            if (mapping.LocalEntityId != adoption.LocalEntityId || mapping.ContentHash != revision.ContentHash)
            {
                throw new ConflictException("The current revision already maps to another tenant record.");
            }

            mapping.BaselineFieldsJson = nextBaseline;
        }

        return mapping;
    }

    private static void ValidateRequest(ApplyCatalogueRevisionFieldsRequest request)
    {
        if (request is null || request.ExpectedSessionVersion < 1 || string.IsNullOrWhiteSpace(request.TemplateId) ||
            request.TemplateId.Length > CatalogueCurrentRevisionBatchLimits.MaximumTemplateIdLength ||
            request.AdoptedRevision < 1 || request.CurrentRevision < 1 ||
            string.IsNullOrWhiteSpace(request.CurrentContentHash) || request.CurrentContentHash.Length > MaximumContentHashLength ||
            string.IsNullOrWhiteSpace(request.ExpectedLocalHash) || request.ExpectedLocalHash.Length != 64 ||
            request.FieldPaths is null || request.FieldPaths.Count is < 1 or > MaximumFieldPaths ||
            request.FieldPaths.Any(path => string.IsNullOrWhiteSpace(path) || path.Length > MaximumFieldPathLength) ||
            request.FieldPaths.Distinct(StringComparer.Ordinal).Count() != request.FieldPaths.Count)
        {
            throw new BadRequestException("Revision field update request is invalid.");
        }
    }

    private static bool IsRevisionWriteConflict(Exception exception)
    {
        var sqlState = exception is PostgresException postgres
            ? postgres.SqlState
            : exception is DbUpdateException { InnerException: PostgresException inner }
                ? inner.SqlState
                : null;
        return sqlState is PostgresErrorCodes.SerializationFailure or PostgresErrorCodes.DeadlockDetected or
            PostgresErrorCodes.UniqueViolation;
    }

}
