using System.Text.Json;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.Catalogue.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.Catalogue.Services;

public sealed partial class CatalogueImportPreviewService
{
    private static CataloguePreviewSource ReadSource(CatalogueImportSessionTemplate item, string locale)
    {
        var revision = CatalogueSessionMapper.ParseRevision(item.RevisionJson);
        var decision = item.DecisionJson is null
            ? null
            : JsonSerializer.Deserialize<CatalogueImportItemDecision>(item.DecisionJson, JsonOptions);
        return new CataloguePreviewSource(item, revision, CatalogueSessionMapper.Localized(revision, locale), decision);
    }

    private CatalogueImportPreviewItemDto BuildPreviewItem(
        CatalogueImportSession session,
        CataloguePreviewSource source,
        Dictionary<(string SourceTemplateId, int SourceRevision), CataloguePreviewAdoption> mappedBySource,
        HashSet<CatalogueLocalEntityKey> existingEntities,
        IReadOnlyCollection<CatalogueRejectedMatch> rejectedMatches,
        IReadOnlyDictionary<(string Type, string Name), List<CatalogueLocalCandidateDto>> candidatesBySource)
    {
        var item = source.Item;
        mappedBySource.TryGetValue((item.TemplateId, item.Revision), out var mapping);
        var mappingIsUsable = mapping is not null && existingEntities.Contains(
            new CatalogueLocalEntityKey(mapping.LocalEntityType, mapping.LocalEntityId));
        var mappedId = mappingIsUsable ? mapping!.LocalEntityId : (Guid?)null;
        var candidates = RemoveRejected(item.TemplateId, item.Revision, rejectedMatches,
            candidatesBySource.GetValueOrDefault(CatalogueImportCandidateLookup.Key(item.Type, source.Localized.Name)) ?? []);
        var warnings = new List<CatalogueImportIssueDto>();
        var blockers = new List<CatalogueImportIssueDto>();
        AddPendingBlockers(session, source, mapping, mappedBySource, candidates, existingEntities, warnings, blockers);
        AddSelectedWarnings(source, warnings, blockers);
        return new CatalogueImportPreviewItemDto(item.TemplateId, item.Revision, item.Type, source.Localized.Name,
            item.IsSelected, source.Decision?.Resolution ?? (mappingIsUsable ? ReuseResolution : null),
            source.Decision?.LocalEntityId ?? mappedId, candidates, warnings, blockers);
    }

    private void AddPendingBlockers(
        CatalogueImportSession session,
        CataloguePreviewSource source,
        CataloguePreviewAdoption? mapping,
        Dictionary<(string SourceTemplateId, int SourceRevision), CataloguePreviewAdoption> mappedBySource,
        List<CatalogueLocalCandidateDto> candidates,
        HashSet<CatalogueLocalEntityKey> existingEntities,
        List<CatalogueImportIssueDto> warnings,
        List<CatalogueImportIssueDto> blockers)
    {
        var item = source.Item;
        if (!item.IsSelected || item.Status is not (CatalogueImportItemStatus.Pending or CatalogueImportItemStatus.Failed)) return;
        var mappingIsUsable = mapping is not null && existingEntities.Contains(
            new CatalogueLocalEntityKey(mapping.LocalEntityType, mapping.LocalEntityId));
        if (item.Type == "cuisine-pack")
        {
            AddPackOfferBlocker(session, blockers);
            return;
        }

        AddResolutionBlockers(source, mapping, mappingIsUsable, candidates, existingEntities, warnings, blockers);
        AddChoiceReviewBlockers(source, mappingIsUsable, blockers);
        if (NeedsOptionSetMaterialization(item.Type, source.Revision, source.Decision, mappingIsUsable)
            && !tenantFeatures.OptionSetMaterializationEnabled)
        {
            blockers.Add(Issue("OPTION_SET_MATERIALIZATION_DISABLED",
                "Option-set imports are not enabled for this tenant. No local menu rows were changed."));
        }

        if (NeedsCreateReview(source.Decision, mappingIsUsable))
        {
            AddDependencyBlockers(session, source, mappedBySource, existingEntities, blockers);
        }
    }

    private static void AddResolutionBlockers(
        CataloguePreviewSource source,
        CataloguePreviewAdoption? mapping,
        bool mappingIsUsable,
        List<CatalogueLocalCandidateDto> candidates,
        HashSet<CatalogueLocalEntityKey> existingEntities,
        List<CatalogueImportIssueDto> warnings,
        List<CatalogueImportIssueDto> blockers)
    {
        var item = source.Item;
        var decision = source.Decision;
        if (mapping is not null && !mappingIsUsable)
        {
            blockers.Add(Issue("MAPPED_LOCAL_RECORD_MISSING", "The previously mapped tenant record no longer exists."));
            return;
        }

        if (mappingIsUsable && decision is not null && ConflictsWithSourceMapping(decision, mapping!.LocalEntityId))
        {
            blockers.Add(Issue("SOURCE_MAPPING_ALREADY_EXISTS",
                "This source revision is already mapped to a tenant record. Start a new-copy session to create a separate record."));
            return;
        }

        if (decision is null)
        {
            if (mappingIsUsable) return;
            blockers.Add(Issue("RESOLUTION_REQUIRED", "Choose whether to create a tenant record or reuse a candidate."));
            if (candidates.Count > 0)
                warnings.Add(Issue("NAME_MATCH_REQUIRES_DECISION", "Name matches are suggestions; confirm a record before reusing it."));
            return;
        }

        if (decision.Resolution.Equals(CreateResolution, StringComparison.OrdinalIgnoreCase))
        {
            CatalogueImportReviewRules.AddLocalReviewBlockers(item.Type, decision, blockers);
        }
        else if (decision.Resolution.Equals(ReuseResolution, StringComparison.OrdinalIgnoreCase) &&
                 !existingEntities.Contains(new CatalogueLocalEntityKey(
                     CatalogueImportReviewRules.ExpectedEntityType(item.Type), decision.LocalEntityId ?? Guid.Empty)))
        {
            blockers.Add(Issue("REUSE_TARGET_MISSING", "The selected tenant record no longer exists."));
        }
    }

    private static void AddChoiceReviewBlockers(
        CataloguePreviewSource source,
        bool mappingIsUsable,
        List<CatalogueImportIssueDto> blockers)
    {
        var decision = source.Decision;
        if (source.Item.Type == OptionSetType && NeedsCreateReview(decision, mappingIsUsable))
        {
            CatalogueImportReviewRules.AddChoiceReviewBlockers(source.Revision,
                decision ?? new CatalogueImportItemDecision { Resolution = CreateResolution }, blockers);
        }

        if (decision?.Resolution.Equals(CreateResolution, StringComparison.OrdinalIgnoreCase) == true &&
            source.Item.Type != OptionSetType)
        {
            CatalogueImportReviewRules.AddChoiceReviewBlockers(source.Revision, decision, blockers);
        }
    }

    private static void AddSelectedWarnings(
        CataloguePreviewSource source,
        List<CatalogueImportIssueDto> warnings,
        List<CatalogueImportIssueDto> blockers)
    {
        if (source.Item.IsSelected)
        {
            CatalogueImportReviewRules.AddTemplateQualityBlocker(source.Revision, blockers);
            CatalogueImportReviewRules.AddUnsupportedPayloadBlockers(source.Revision, blockers);
            if (source.Item.Type == OptionSetType)
                warnings.Add(Issue("OPTION_SET_REVIEW_REQUIRED", "Confirm local choice prices and every tenant-specific operational detail before activating offers."));
        }

        if (HasCentralMedia(source.Revision))
            warnings.Add(Issue("CENTRAL_MEDIA_NOT_IMPORTED", "Central media stays separate; add or select tenant-approved images in the product editor."));
    }

    private static bool HasCentralMedia(CentralCatalogueTemplateRevision revision) =>
        revision.Provenance.ValueKind == JsonValueKind.Object &&
        revision.Provenance.TryGetProperty("mediaAssets", out var assets) &&
        assets.ValueKind == JsonValueKind.Array && assets.GetArrayLength() > 0;

    private static void AddPackOfferBlocker(CatalogueImportSession session, List<CatalogueImportIssueDto> blockers)
    {
        if (!session.Templates.Any(candidate => candidate.IsSelected && candidate.SelectionRole == "offer"))
            blockers.Add(Issue("PACK_OFFER_REQUIRED", "Select at least one offer from this cuisine pack."));
    }

    private static bool ConflictsWithSourceMapping(CatalogueImportItemDecision decision, Guid mappedId) =>
        decision.Resolution.Equals(CreateResolution, StringComparison.OrdinalIgnoreCase) ||
        decision.Resolution.Equals(ReuseResolution, StringComparison.OrdinalIgnoreCase) && decision.LocalEntityId != mappedId;

    private static bool NeedsCreateReview(CatalogueImportItemDecision? decision, bool hasUsableMapping) =>
        decision is null ? !hasUsableMapping
            : decision.Resolution.Equals(CreateResolution, StringComparison.OrdinalIgnoreCase);

    private static bool NeedsOptionSetMaterialization(
        string templateType,
        CentralCatalogueTemplateRevision revision,
        CatalogueImportItemDecision? decision,
        bool hasUsableMapping)
    {
        if (hasUsableMapping ||
            decision?.Resolution.Equals(ReuseResolution, StringComparison.OrdinalIgnoreCase) == true ||
            decision?.Resolution.Equals(CreateResolution, StringComparison.OrdinalIgnoreCase) != true)
        {
            return false;
        }

        if (templateType == OptionSetType)
        {
            return true;
        }

        if (templateType == "item" && revision.Payload.ValueKind == JsonValueKind.Object)
        {
            return HasReferences(revision.Payload, "optionSets") || HasReferences(revision.Payload, "sideSets");
        }

        if (templateType == "bundle") return true;

        return false;
    }

    private static bool HasReferences(JsonElement payload, string property) =>
        payload.TryGetProperty(property, out var references) && references.ValueKind == JsonValueKind.Array &&
        references.GetArrayLength() > 0;

    private static List<CatalogueLocalCandidateDto> RemoveRejected(
        string templateId,
        int revision,
        IReadOnlyCollection<CatalogueRejectedMatch> rejected,
        List<CatalogueLocalCandidateDto> candidates) =>
        candidates.Where(candidate => !rejected.Any(decision =>
            decision.SourceTemplateId == templateId && decision.SourceRevision == revision &&
            decision.CandidateId == candidate.Id &&
            decision.CandidateType == candidate.EntityType)).ToList();

    private static CatalogueImportIssueDto Issue(string code, string message) => new(code, message);
}
