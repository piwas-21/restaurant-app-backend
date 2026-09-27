using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Catalogue.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Catalogue.Services;

public sealed class CatalogueImportStateStore(
    ApplicationDbContext context,
    ICurrentUserService currentUser) : ICatalogueImportStateStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<CatalogueImportSession> LoadAsync(Guid sessionId, CancellationToken cancellationToken) =>
        await context.CatalogueImportSessions.Include(session => session.Templates)
            .FirstOrDefaultAsync(session => session.Id == sessionId, cancellationToken)
        ?? throw new NotFoundException("Catalogue import session was not found");

    public Task<CatalogueImportBatchContext> LoadBatchContextAsync(
        CatalogueImportSession session,
        CancellationToken cancellationToken) =>
        CatalogueImportBatchContext.LoadAsync(context, session, cancellationToken);

    public CatalogueImportResultDto ReadCachedResult(CatalogueImportSession session)
    {
        if (string.IsNullOrWhiteSpace(session.LastImportResultJson))
        {
            throw new ConflictException("Completed import result is unavailable; reload the session.");
        }

        return JsonSerializer.Deserialize<CatalogueImportResultDto>(session.LastImportResultJson, JsonOptions)
            ?? throw new ConflictException("Completed import result is invalid; reload the session.");
    }

    public Task<IReadOnlyList<CatalogueTemplateAdoption>> AddOutcomeMappingsAsync(
        CatalogueImportSession session,
        CatalogueImportSessionTemplate item,
        CatalogueTemplateImportOutcome outcome,
        CatalogueImportBatchContext batchContext,
        CancellationToken cancellationToken)
    {
        var candidates = new List<CatalogueTemplateAdoption>();
        if (outcome.LocalEntityType is not null && outcome.LocalEntityId is Guid localEntityId)
        {
            candidates.Add(CreateMapping(session, item.TemplateId, item.Revision, null,
                outcome.LocalEntityType, localEntityId, item.ContentHash));
        }

        foreach (var mapping in outcome.EntryMappings)
        {
            candidates.Add(CreateMapping(session, mapping.SourceTemplateId, mapping.SourceRevision,
                mapping.SourceEntryId, mapping.LocalEntityType, mapping.LocalEntityId,
                mapping.ContentHash));
        }

        if (candidates.Count == 0)
        {
            return Task.FromResult<IReadOnlyList<CatalogueTemplateAdoption>>([]);
        }

        var added = new List<CatalogueTemplateAdoption>();

        foreach (var candidate in candidates)
        {
            if (batchContext.TryGetOwnMapping(candidate, out var existingMapping))
            {
                if (existingMapping!.LocalEntityId != candidate.LocalEntityId ||
                    existingMapping.ContentHash != candidate.ContentHash)
                {
                    throw new ConflictException("An immutable catalogue source mapping already points to another tenant record.");
                }

                continue;
            }

            candidate.IsDefault = !session.CreateNewCopy && !batchContext.HasDefaultMapping(candidate);
            context.CatalogueTemplateAdoptions.Add(candidate);
            added.Add(candidate);
        }

        return Task.FromResult<IReadOnlyList<CatalogueTemplateAdoption>>(added);
    }

    public async Task SaveSkippedAsync(
        CatalogueImportSession session,
        IReadOnlyList<CatalogueImportSessionTemplate> items,
        CancellationToken cancellationToken)
    {
        if (items.Count == 0)
        {
            return;
        }

        AttachSessionIfDetached(session);
        foreach (var item in items)
        {
            item.Status = CatalogueImportItemStatus.Skipped;
            item.FailureCode = "NOT_SELECTED";
            item.UpdatedAt = DateTime.UtcNow;
        }

        session.Version += items.Count;
        session.UpdatedAt = DateTime.UtcNow;
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task SaveFailedAsync(
        CatalogueImportSession session,
        CatalogueImportSessionTemplate item,
        string failureCode,
        CancellationToken cancellationToken)
    {
        AttachSessionIfDetached(session);
        item.Status = CatalogueImportItemStatus.Failed;
        item.FailureCode = failureCode;
        item.UpdatedAt = DateTime.UtcNow;
        session.Version++;
        session.UpdatedAt = DateTime.UtcNow;
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<CatalogueImportResultDto> CompleteAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        context.ChangeTracker.Clear();
        var session = await LoadAsync(sessionId, cancellationToken);
        var actionable = session.Templates.Where(item => item.IsSelected && item.Type != "cuisine-pack").ToArray();
        var failures = actionable.Count(item => item.Status == CatalogueImportItemStatus.Failed);
        var successes = actionable.Count(item => item.Status == CatalogueImportItemStatus.Imported);
        session.Status = ResolveCompletionStatus(failures, successes);
        session.CompletedAt = DateTime.UtcNow;
        session.Version++;
        session.UpdatedAt = DateTime.UtcNow;
        var result = BuildResult(session);
        session.LastImportResultJson = JsonSerializer.Serialize(result, JsonOptions);
        await context.SaveChangesAsync(cancellationToken);
        return result;
    }

    private void AttachSessionIfDetached(CatalogueImportSession session)
    {
        if (context.Entry(session).State == EntityState.Detached)
        {
            context.Attach(session);
        }
    }

    private CatalogueTemplateAdoption CreateMapping(
        CatalogueImportSession session,
        string sourceTemplateId,
        int sourceRevision,
        string? sourceEntryId,
        string localEntityType,
        Guid localEntityId,
        string contentHash)
    {
        var sourceTemplate = session.Templates.FirstOrDefault(value =>
            value.TemplateId == sourceTemplateId && value.Revision == sourceRevision);
        var revision = sourceTemplate is null ? null : CatalogueSessionMapper.ParseRevision(sourceTemplate.RevisionJson);
        return new CatalogueTemplateAdoption
        {
            Id = Guid.NewGuid(),
            AdoptionId = session.AdoptionId,
            SessionId = session.Id,
            SourceTemplateId = sourceTemplateId,
            SourceRevision = sourceRevision,
            SourceEntryId = sourceEntryId,
            LocalEntityType = localEntityType,
            LocalEntityId = localEntityId,
            ContentHash = contentHash,
            BaselineFieldsJson = revision is null ? null : CatalogueRevisionBaseline.Create(revision, revision.Type),
            CreatedAt = DateTime.UtcNow,
            CreatedBy = currentUser.GetAuditIdentifier()
        };
    }

    private static CatalogueImportResultDto BuildResult(CatalogueImportSession session) => new(
        session.Id,
        session.Version,
        session.Status.ToString(),
        session.Templates.OrderBy(item => item.IsRoot ? 0 : 1)
            .ThenBy(item => item.TemplateId, StringComparer.Ordinal)
            .Select(item => new CatalogueImportItemResultDto(
                item.TemplateId,
                item.Revision,
                item.Status.ToString(),
                item.LocalEntityType,
                item.LocalEntityId,
                item.FailureCode))
            .ToArray());

    private static CatalogueImportStatus ResolveCompletionStatus(int failures, int successes)
    {
        if (failures == 0) return CatalogueImportStatus.Imported;
        return successes > 0 ? CatalogueImportStatus.PartiallyImported : CatalogueImportStatus.Failed;
    }
}
