using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.Catalogue.Services;

internal sealed class CatalogueImportEntityResolver(CatalogueImportBatchContext batch)
{
    public CatalogueTemplateAdoption? FindReusableMapping(
        string templateId,
        int revision,
        string expectedEntityType)
    {
        var mapping = batch.FindReusableMapping(templateId, revision, expectedEntityType);
        if (mapping is not null && !batch.LocalEntityExists(expectedEntityType, mapping.LocalEntityId))
        {
            throw new BadRequestException(
                "The source mapping points to a tenant record that no longer exists.",
                "MAPPED_LOCAL_RECORD_MISSING");
        }

        return mapping;
    }

    public Guid ResolveDependency(
        CatalogueImportSession session,
        CatalogueSourceReference reference,
        string expectedEntityType)
    {
        var template = session.Templates.FirstOrDefault(item =>
            item.TemplateId == reference.TemplateId && item.Revision == reference.Revision);
        if (template is { Status: CatalogueImportItemStatus.Imported, LocalEntityId: Guid importedId })
        {
            if (template.LocalEntityType != expectedEntityType || !batch.LocalEntityExists(expectedEntityType, importedId))
            {
                throw new BadRequestException(
                    "An imported catalogue dependency no longer resolves to the expected tenant record.",
                    "LOCAL_DEPENDENCY_MISSING");
            }

            return importedId;
        }

        var mapping = batch.FindDependencyMapping(reference.TemplateId, reference.Revision, expectedEntityType);
        if (mapping is null) throw DependencyNotReady(reference);
        return mapping.LocalEntityId;
    }

    public bool LocalEntityExists(string? entityType, Guid id) => batch.LocalEntityExists(entityType, id);

    private static BadRequestException DependencyNotReady(CatalogueSourceReference reference) => new(
        $"The selected dependency {reference.Key} has no imported tenant mapping.",
        "LOCAL_DEPENDENCY_MISSING");
}
