using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.TenantFeatures;
using RestaurantSystem.Api.Features.Catalogue.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Catalogue.Services;

public sealed partial class CatalogueImportPreviewService(
    ApplicationDbContext context,
    ITenantFeatures tenantFeatures,
    ILogger<CatalogueImportPreviewService> logger) : ICatalogueImportPreviewService
{
    private const string CreateResolution = "Create";
    private const string ReuseResolution = "Reuse";
    private const string OptionSetType = "option-set";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip
    };

    public async Task<CatalogueImportPreviewDto> PreviewAsync(
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        var session = await context.CatalogueImportSessions.AsNoTracking()
            .Include(value => value.Templates)
            .FirstOrDefaultAsync(value => value.Id == sessionId, cancellationToken)
            ?? throw new NotFoundException("Catalogue import session was not found");
        var sourceItems = session.Templates.OrderBy(item => item.IsRoot ? 0 : 1)
            .ThenBy(item => item.TemplateId, StringComparer.Ordinal)
            .Select(item => ReadSource(item, session.Locale))
            .ToArray();
        var sourceRevisions = sourceItems
            .Select(source => (source.Item.TemplateId, source.Item.Revision))
            .Distinct()
            .ToArray();
        var mappings = await context.CatalogueTemplateAdoptions.AsNoTracking()
            .Where(adoption => adoption.SourceEntryId == null &&
                (adoption.AdoptionId == session.AdoptionId || (!session.CreateNewCopy && adoption.IsDefault)))
            .Where(CatalogueSourceRevisionPredicate.ForAdoptions(sourceRevisions))
            .Select(adoption => new CataloguePreviewAdoption(
                adoption.SourceTemplateId,
                adoption.SourceRevision,
                adoption.AdoptionId,
                adoption.LocalEntityType,
                adoption.LocalEntityId,
                adoption.IsDefault))
            .ToListAsync(cancellationToken);
        var rejectedMatches = await context.CatalogueMatchDecisions.AsNoTracking()
            .Where(decision => decision.Decision == CatalogueMatchDecisionStatus.Rejected)
            .Where(CatalogueSourceRevisionPredicate.ForMatchDecisions(sourceRevisions))
            .Select(decision => new CatalogueRejectedMatch(
                decision.SourceTemplateId,
                decision.SourceRevision,
                decision.CandidateType,
                decision.CandidateId))
            .ToListAsync(cancellationToken);
        var entityReferences = mappings.Select(mapping =>
                new CatalogueLocalEntityKey(mapping.LocalEntityType, mapping.LocalEntityId))
            .Concat(sourceItems.Where(source =>
                    source.Decision?.Resolution.Equals("Reuse", StringComparison.OrdinalIgnoreCase) == true &&
                    source.Decision.LocalEntityId.HasValue)
                .Select(source => new CatalogueLocalEntityKey(
                    CatalogueImportReviewRules.ExpectedEntityType(source.Item.Type),
                    source.Decision!.LocalEntityId!.Value)));
        var existingEntities = await CatalogueImportEntityLookup.LoadExistingAsync(
            context, entityReferences, cancellationToken);
        var candidatesBySource = await CatalogueImportCandidateLookup.LoadAsync(
            context,
            sourceItems.Select(source => new CatalogueCandidateSearch(source.Item.Type, source.Localized.Name)),
            cancellationToken);
        var mappedBySource = mappings
            .OrderBy(adoption => adoption.AdoptionId == session.AdoptionId ? 0 : 1)
            .ThenByDescending(adoption => adoption.IsDefault)
            .GroupBy(adoption => (adoption.SourceTemplateId, adoption.SourceRevision))
            .ToDictionary(group => group.Key, group => group.First());
        var results = sourceItems.Select(source => BuildPreviewItem(
            session, source, mappedBySource, existingEntities, rejectedMatches, candidatesBySource)).ToArray();

        logger.LogDebug("Built catalogue preview for {SessionId} with {ItemCount} template items", sessionId, results.Length);
        return new CatalogueImportPreviewDto(session.Id, session.Version, results);
    }


}

internal sealed record CataloguePreviewAdoption(
    string SourceTemplateId,
    int SourceRevision,
    Guid AdoptionId,
    string LocalEntityType,
    Guid LocalEntityId,
    bool IsDefault);

internal sealed record CatalogueRejectedMatch(
    string SourceTemplateId,
    int SourceRevision,
    string CandidateType,
    Guid CandidateId);

internal sealed record CataloguePreviewSource(
    CatalogueImportSessionTemplate Item,
    CentralCatalogueTemplateRevision Revision,
    (string Name, string? Description) Localized,
    CatalogueImportItemDecision? Decision);
