using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.Catalogue.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.Catalogue.Services;

public sealed partial class CatalogueImportPreviewService
{
    private static void AddDependencyBlockers(
        CatalogueImportSession session,
        CataloguePreviewSource source,
        Dictionary<(string SourceTemplateId, int SourceRevision), CataloguePreviewAdoption> mappedBySource,
        HashSet<CatalogueLocalEntityKey> existingEntities,
        CatalogueImportReuseKinds reuseKinds,
        List<CatalogueImportIssueDto> blockers)
    {
        IReadOnlyList<CataloguePayloadDependency> dependencies;
        try
        {
            if (source.Revision.Type == "item" &&
                CatalogueImportPayloadReader.ReadOptionalReference(source.Revision.Payload, "category") is null)
            {
                blockers.Add(Issue("ITEM_CATEGORY_REQUIRED", "Assign a tenant category before importing this item."));
            }

            dependencies = CatalogueImportPayloadReader.ReadMaterializationDependencies(source.Revision);
        }
        catch (BadRequestException)
        {
            // The selected-payload validator already emits the actionable unsupported-payload issue.
            return;
        }

        AddDuplicateResolvedOptionBlocker(session, source, dependencies, mappedBySource, existingEntities, blockers);

        foreach (var dependency in dependencies)
        {
            AddDependencyBlocker(session, dependency, mappedBySource, existingEntities, reuseKinds, blockers);
        }
    }

    private static void AddDependencyBlocker(
        CatalogueImportSession session,
        CataloguePayloadDependency dependency,
        Dictionary<(string SourceTemplateId, int SourceRevision), CataloguePreviewAdoption> mappedBySource,
        HashSet<CatalogueLocalEntityKey> existingEntities,
        CatalogueImportReuseKinds reuseKinds,
        List<CatalogueImportIssueDto> blockers)
    {
        var template = session.Templates.FirstOrDefault(candidate =>
            candidate.TemplateId == dependency.Reference.TemplateId &&
            candidate.Revision == dependency.Reference.Revision);
        if (template is null)
        {
            blockers.Add(Issue("DEPENDENCY_NOT_IN_SESSION",
                $"The required catalogue dependency {dependency.Reference.Key} is missing from this import session."));
            return;
        }

        if (!template.Type.Equals(dependency.ExpectedTemplateType, StringComparison.Ordinal))
        {
            blockers.Add(Issue("DEPENDENCY_TYPE_MISMATCH",
                $"The catalogue dependency {dependency.Reference.Key} must be a {dependency.ExpectedTemplateType} template."));
            return;
        }

        if (template.IsSelected)
        {
            AddSelectedDependencyBlocker(template, dependency.Reference, existingEntities, reuseKinds, blockers);
            return;
        }

        var entityType = CatalogueImportReviewRules.ExpectedEntityType(template.Type);
        var hasReusableMapping = mappedBySource.TryGetValue(
                (template.TemplateId, template.Revision), out var mapping) &&
            mapping.LocalEntityType == entityType &&
            existingEntities.Contains(new CatalogueLocalEntityKey(entityType, mapping.LocalEntityId));
        if (hasReusableMapping && !reuseKinds.IsCompatible(
                CatalogueSessionMapper.ParseRevision(template.RevisionJson), mapping!.LocalEntityId))
        {
            blockers.Add(Issue("REUSE_KIND_MISMATCH",
                $"The mapped catalogue dependency {dependency.Reference.Key} has a different choice or ingredient kind."));
            return;
        }
        if (!hasReusableMapping)
        {
            blockers.Add(Issue("DEPENDENCY_NOT_SELECTED",
                $"Select the required catalogue dependency {dependency.Reference.Key} or adopt an existing mapped record."));
        }
    }

    private static void AddSelectedDependencyBlocker(
        CatalogueImportSessionTemplate template,
        CatalogueSourceReference reference,
        HashSet<CatalogueLocalEntityKey> existingEntities,
        CatalogueImportReuseKinds reuseKinds,
        List<CatalogueImportIssueDto> blockers)
    {
        if (template.Status == CatalogueImportItemStatus.Failed)
        {
            blockers.Add(Issue("DEPENDENCY_IMPORT_FAILED",
                $"The required catalogue dependency {reference.Key} must be fixed before importing this offer."));
            return;
        }

        if (template.Status == CatalogueImportItemStatus.Skipped)
        {
            blockers.Add(Issue("DEPENDENCY_NOT_IMPORTED",
                $"The selected catalogue dependency {reference.Key} was skipped and must be reselected."));
            return;
        }

        if (template.Status != CatalogueImportItemStatus.Imported) return;
        var expectedEntityType = CatalogueImportReviewRules.ExpectedEntityType(template.Type);
        if (template.LocalEntityType != expectedEntityType || template.LocalEntityId is not Guid localId ||
            !existingEntities.Contains(new CatalogueLocalEntityKey(expectedEntityType, localId)))
        {
            blockers.Add(Issue("DEPENDENCY_LOCAL_RECORD_MISSING",
                $"The imported catalogue dependency {reference.Key} no longer maps to a compatible tenant record."));
            return;
        }
        if (!reuseKinds.IsCompatible(CatalogueSessionMapper.ParseRevision(template.RevisionJson), localId))
        {
            blockers.Add(Issue("REUSE_KIND_MISMATCH",
                $"The imported catalogue dependency {reference.Key} has a different choice or ingredient kind."));
        }
    }

    private static void AddDuplicateResolvedOptionBlocker(
        CatalogueImportSession session,
        CataloguePreviewSource source,
        IReadOnlyList<CataloguePayloadDependency> dependencies,
        Dictionary<(string SourceTemplateId, int SourceRevision), CataloguePreviewAdoption> mappedBySource,
        HashSet<CatalogueLocalEntityKey> existingEntities,
        List<CatalogueImportIssueDto> blockers)
    {
        if (source.Item.Type != "option-set") return;

        var resolvedKeys = new HashSet<CatalogueLocalEntityKey>();
        foreach (var reference in dependencies.Select(dependency => dependency.Reference))
        {
            var localKey = ResolveLocalOptionKey(session, reference, mappedBySource);
            if (localKey is not CatalogueLocalEntityKey key || !existingEntities.Contains(key)) continue;
            if (!resolvedKeys.Add(key))
            {
                blockers.Add(Issue("DUPLICATE_RESOLVED_OPTION",
                    "Distinct catalogue choices resolve to the same tenant record. Choose distinct tenant records before importing."));
                return;
            }
        }
    }

    private static CatalogueLocalEntityKey? ResolveLocalOptionKey(
        CatalogueImportSession session,
        CatalogueSourceReference reference,
        Dictionary<(string SourceTemplateId, int SourceRevision), CataloguePreviewAdoption> mappedBySource)
    {
        var template = session.Templates.FirstOrDefault(candidate =>
            candidate.TemplateId == reference.TemplateId && candidate.Revision == reference.Revision);
        if (template is null) return null;
        var entityType = CatalogueImportReviewRules.ExpectedEntityType(template.Type);
        if (template.IsSelected)
        {
            if (template.Status == CatalogueImportItemStatus.Imported &&
                template.LocalEntityType == entityType && template.LocalEntityId is Guid importedId)
            {
                return new CatalogueLocalEntityKey(entityType, importedId);
            }

            if (string.IsNullOrWhiteSpace(template.DecisionJson)) return null;
            var decision = CatalogueSessionMapper.ParseDecision(template.DecisionJson);
            return decision?.Resolution.Equals(ReuseResolution, StringComparison.OrdinalIgnoreCase) == true &&
                   decision.LocalEntityId is Guid reusedId
                ? new CatalogueLocalEntityKey(entityType, reusedId)
                : null;
        }

        return mappedBySource.TryGetValue((template.TemplateId, template.Revision), out var mapping) &&
               mapping.LocalEntityType == entityType
            ? new CatalogueLocalEntityKey(entityType, mapping.LocalEntityId)
            : null;
    }
}
