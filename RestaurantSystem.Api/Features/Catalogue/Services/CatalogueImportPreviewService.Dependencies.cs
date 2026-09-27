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

        foreach (var dependency in dependencies)
        {
            var template = session.Templates.FirstOrDefault(candidate =>
                candidate.TemplateId == dependency.Reference.TemplateId &&
                candidate.Revision == dependency.Reference.Revision);
            if (template is null)
            {
                blockers.Add(Issue("DEPENDENCY_NOT_IN_SESSION",
                    $"The required catalogue dependency {dependency.Reference.Key} is missing from this import session."));
                continue;
            }

            if (!template.Type.Equals(dependency.ExpectedTemplateType, StringComparison.Ordinal))
            {
                blockers.Add(Issue("DEPENDENCY_TYPE_MISMATCH",
                    $"The catalogue dependency {dependency.Reference.Key} must be a {dependency.ExpectedTemplateType} template."));
                continue;
            }

            if (template.IsSelected && template.Status == CatalogueImportItemStatus.Failed)
            {
                blockers.Add(Issue("DEPENDENCY_IMPORT_FAILED",
                    $"The required catalogue dependency {dependency.Reference.Key} must be fixed before importing this offer."));
                continue;
            }

            if (template.IsSelected)
            {
                if (template.Status == CatalogueImportItemStatus.Skipped)
                {
                    blockers.Add(Issue("DEPENDENCY_NOT_IMPORTED",
                        $"The selected catalogue dependency {dependency.Reference.Key} was skipped and must be reselected."));
                    continue;
                }

                if (template.Status == CatalogueImportItemStatus.Imported)
                {
                    var expectedEntityType = CatalogueImportReviewRules.ExpectedEntityType(template.Type);
                    if (template.LocalEntityType != expectedEntityType || template.LocalEntityId is not Guid localId ||
                        !existingEntities.Contains(new CatalogueLocalEntityKey(expectedEntityType, localId)))
                    {
                        blockers.Add(Issue("DEPENDENCY_LOCAL_RECORD_MISSING",
                            $"The imported catalogue dependency {dependency.Reference.Key} no longer maps to a compatible tenant record."));
                    }
                }

                continue;
            }

            var entityType = CatalogueImportReviewRules.ExpectedEntityType(template.Type);
            var hasReusableMapping = mappedBySource.TryGetValue(
                    (template.TemplateId, template.Revision), out var mapping) &&
                mapping.LocalEntityType == entityType &&
                existingEntities.Contains(new CatalogueLocalEntityKey(entityType, mapping.LocalEntityId));
            if (!hasReusableMapping)
            {
                blockers.Add(Issue("DEPENDENCY_NOT_SELECTED",
                    $"Select the required catalogue dependency {dependency.Reference.Key} or adopt an existing mapped record."));
            }
        }
    }
}
