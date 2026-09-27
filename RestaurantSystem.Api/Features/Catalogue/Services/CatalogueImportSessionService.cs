using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Catalogue.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Catalogue.Services;

public sealed class CatalogueImportSessionService(
    ApplicationDbContext context,
    ICatalogueTemplateGraphLoader graphLoader,
    ICatalogueImportPreviewService preview,
    ICatalogueSessionImporter importer,
    ICatalogueRejectedCandidateService rejectedCandidates,
    ICurrentUserService currentUser,
    ILogger<CatalogueImportSessionService> logger) : ICatalogueImportSessionService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip
    };

    public async Task<CatalogueImportSessionDto> CreateAsync(
        CreateCatalogueImportSessionRequest request,
        CancellationToken cancellationToken)
    {
        CatalogueImportSessionRules.ValidateCreateRequest(request);
        var idempotencyKey = request.IdempotencyKey.Trim();
        var existing = await FindByIdempotencyKeyAsync(idempotencyKey, cancellationToken);
        if (existing is not null)
        {
            CatalogueImportSessionRules.EnsureSameCreateIntent(existing, request);
            return CatalogueSessionMapper.ToDto(existing);
        }

        var graph = await graphLoader.LoadAsync(request.TemplateId, request.Revision, cancellationToken);
        var adoptionId = await ResolveAdoptionIdAsync(request, cancellationToken);
        var audit = currentUser.GetAuditIdentifier();
        var session = new CatalogueImportSession
        {
            Id = Guid.NewGuid(),
            RootTemplateId = request.TemplateId,
            RootRevision = request.Revision,
            Locale = request.Locale.Trim(),
            IdempotencyKey = idempotencyKey,
            AdoptionId = adoptionId,
            CreateNewCopy = request.CreateNewCopy,
            Status = CatalogueImportStatus.Draft,
            Version = 1,
            CreatedBy = audit,
            Templates = graph.Nodes.Select(node => new CatalogueImportSessionTemplate
            {
                Id = Guid.NewGuid(),
                TemplateId = node.Revision.TemplateId,
                Revision = node.Revision.Revision,
                Type = node.Revision.Type,
                ContentHash = node.Revision.ContentHash,
                RevisionJson = JsonSerializer.Serialize(node.Revision, JsonOptions),
                IsRoot = node.IsRoot,
                IsSelectable = node.IsSelectable,
                SelectionRole = node.SelectionRole,
                CreatedBy = audit
            }).ToList()
        };
        CatalogueSessionSelection.Recompute(session, request.SelectedTemplateIds);
        session.CreateSelectionJson = CatalogueImportSessionRules.SerializeSelection(session.Templates
            .Where(item => item.IsSelected)
            .Select(item => item.TemplateId));
        context.CatalogueImportSessions.Add(session);

        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
        {
            context.Entry(session).State = EntityState.Detached;
            var winner = await FindByIdempotencyKeyAsync(idempotencyKey, cancellationToken);
            if (winner is null)
            {
                throw;
            }

            logger.LogInformation(exception, "Reused catalogue import session after an idempotency race");
            CatalogueImportSessionRules.EnsureSameCreateIntent(winner, request);
            return CatalogueSessionMapper.ToDto(winner);
        }

        return CatalogueSessionMapper.ToDto(session);
    }

    public async Task<CatalogueImportSessionDto> GetAsync(Guid sessionId, CancellationToken cancellationToken) =>
        CatalogueSessionMapper.ToDto(await GetTrackedOrReadOnlyAsync(sessionId, false, cancellationToken));

    public async Task<CatalogueImportSessionDto> UpdateItemsAsync(
        Guid sessionId,
        UpdateCatalogueImportSessionRequest request,
        CancellationToken cancellationToken)
    {
        var session = await GetTrackedOrReadOnlyAsync(sessionId, true, cancellationToken);
        var isRetry = session.Status is CatalogueImportStatus.PartiallyImported or CatalogueImportStatus.Failed;
        EnsureEditable(session, request.ExpectedVersion, isRetry);
        var selected = PrepareSelection(session, request, isRetry);
        await ApplyDecisionsAsync(session, request.Decisions, selected, isRetry, cancellationToken);

        session.Version++;
        session.UpdatedAt = DateTime.UtcNow;
        session.UpdatedBy = currentUser.GetAuditIdentifier();
        if (isRetry)
        {
            session.LastImportResultJson = null;
            session.CompletedAt = null;
        }

        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConflictException("Import session changed. Reload it before updating selections.");
        }

        return CatalogueSessionMapper.ToDto(session);
    }

    private static void EnsureEditable(CatalogueImportSession session, int expectedVersion, bool isRetry)
    {
        if (session.Version != expectedVersion)
        {
            throw new ConflictException("Import session changed. Reload it before updating selections.");
        }

        if (session.Status != CatalogueImportStatus.Draft && !isRetry)
        {
            throw new ConflictException("An import that has started cannot be edited.");
        }
    }

    private static HashSet<(string TemplateId, int Revision)> PrepareSelection(
        CatalogueImportSession session,
        UpdateCatalogueImportSessionRequest request,
        bool isRetry)
    {
        CatalogueImportSessionRules.ValidateUnique(request.SelectedTemplateIds, "selected template IDs");
        if (request.Decisions.Count > CatalogueImportSessionRules.MaximumSelectedTemplates)
        {
            throw new BadRequestException("Too many import decisions were supplied");
        }

        var currentSelection = session.Templates.Where(item => item.IsSelected)
            .Select(item => (item.TemplateId, item.Revision)).ToHashSet();
        var requestedSelection = CatalogueSessionSelection.ComputeSelectedKeys(session, request.SelectedTemplateIds);
        ValidateRetrySelection(session, request, isRetry, currentSelection, requestedSelection);
        if (!isRetry)
        {
            CatalogueSessionSelection.Recompute(session, request.SelectedTemplateIds);
        }

        return session.Templates.Where(item => item.IsSelected)
            .Select(item => (item.TemplateId, item.Revision)).ToHashSet();
    }

    private static void ValidateRetrySelection(
        CatalogueImportSession session,
        UpdateCatalogueImportSessionRequest request,
        bool isRetry,
        HashSet<(string TemplateId, int Revision)> currentSelection,
        HashSet<(string TemplateId, int Revision)> requestedSelection)
    {
        if (!isRetry) return;
        if (!currentSelection.SetEquals(requestedSelection))
        {
            throw new ConflictException("An import retry must keep the original item selection.");
        }

        if (request.Decisions.Count == 0 || request.Decisions.Any(decision =>
                !session.Templates.Any(item => item.TemplateId == decision.TemplateId &&
                    item.Revision == decision.Revision && item.IsSelected &&
                    item.Status == CatalogueImportItemStatus.Failed)))
        {
            throw new BadRequestException("An import retry can only update selected items that failed.");
        }
    }

    private async Task ApplyDecisionsAsync(
        CatalogueImportSession session,
        IReadOnlyCollection<CatalogueImportItemDecision> decisions,
        HashSet<(string TemplateId, int Revision)> selected,
        bool isRetry,
        CancellationToken cancellationToken)
    {
        var decisionKeys = new HashSet<(string TemplateId, int Revision)>();
        foreach (var decision in decisions)
        {
            var key = (decision.TemplateId, decision.Revision);
            if (!decisionKeys.Add(key) || !selected.Contains(key))
            {
                throw new BadRequestException("Each decision must target one selected template revision");
            }

            var item = session.Templates.Single(candidate =>
                candidate.TemplateId == decision.TemplateId && candidate.Revision == decision.Revision);
            CatalogueImportSessionRules.ValidateDecision(decision, item.Type);
            await rejectedCandidates.SyncAsync(item, session.Locale, decision, cancellationToken);
            item.DecisionJson = CatalogueSessionMapper.SerializeDecision(decision with
            {
                Resolution = decision.Resolution.Equals("reuse", StringComparison.OrdinalIgnoreCase) ? "Reuse" : "Create"
            });
            if (isRetry)
            {
                item.Status = CatalogueImportItemStatus.Pending;
                item.FailureCode = null;
            }
            item.UpdatedAt = DateTime.UtcNow;
            item.UpdatedBy = currentUser.GetAuditIdentifier();
        }
    }

    public Task<CatalogueImportPreviewDto> PreviewAsync(Guid sessionId, CancellationToken cancellationToken) =>
        preview.PreviewAsync(sessionId, cancellationToken);

    public Task<CatalogueImportResultDto> ImportAsync(
        Guid sessionId,
        ImportCatalogueSessionRequest request,
        CancellationToken cancellationToken) => importer.ImportAsync(sessionId, request, cancellationToken);

    private async Task<Guid> ResolveAdoptionIdAsync(
        CreateCatalogueImportSessionRequest request,
        CancellationToken cancellationToken)
    {
        if (request.CreateNewCopy)
        {
            return Guid.NewGuid();
        }

        return await context.CatalogueTemplateAdoptions.AsNoTracking()
            .Where(adoption => adoption.SourceTemplateId == request.TemplateId && adoption.IsDefault)
            .OrderByDescending(adoption => adoption.SourceRevision)
            .ThenByDescending(adoption => adoption.CreatedAt)
            .Select(adoption => (Guid?)adoption.AdoptionId)
            .FirstOrDefaultAsync(cancellationToken) ?? Guid.NewGuid();
    }

    private Task<CatalogueImportSession?> FindByIdempotencyKeyAsync(
        string key,
        CancellationToken cancellationToken) => context.CatalogueImportSessions
        .AsNoTracking()
        .Include(session => session.Templates)
        .FirstOrDefaultAsync(session => session.IdempotencyKey == key, cancellationToken);

    private async Task<CatalogueImportSession> GetTrackedOrReadOnlyAsync(
        Guid sessionId,
        bool tracked,
        CancellationToken cancellationToken)
    {
        var query = context.CatalogueImportSessions.Include(session => session.Templates).AsQueryable();
        if (!tracked)
        {
            query = query.AsNoTracking();
        }

        return await query.FirstOrDefaultAsync(session => session.Id == sessionId, cancellationToken)
            ?? throw new NotFoundException("Catalogue import session was not found");
    }

}
