using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Catalogue.Services;

internal sealed partial class CatalogueRevisionLocalTextReader(ApplicationDbContext context)
{
    public async Task<Dictionary<string, string?>> ReadAsync(
        CatalogueTemplateAdoption adoption,
        string templateType,
        IReadOnlyCollection<CatalogueTemplateAdoption> entryMappings,
        CancellationToken cancellationToken)
    {
        if (CatalogueRevisionTemplateTypes.ForEntity(adoption.LocalEntityType) != templateType)
        {
            return new Dictionary<string, string?>(StringComparer.Ordinal);
        }

        var result = await ReadManyAsync([adoption], entryMappings, cancellationToken);
        return result.GetValueOrDefault(adoption.Id, new Dictionary<string, string?>(StringComparer.Ordinal));
    }

    public async Task<Dictionary<Guid, Dictionary<string, string?>>> ReadManyAsync(
        IReadOnlyCollection<CatalogueTemplateAdoption> adoptions,
        IReadOnlyCollection<CatalogueTemplateAdoption> entryMappings,
        CancellationToken cancellationToken)
    {
        var fieldsByAdoption = adoptions.ToDictionary(
            adoption => adoption.Id,
            _ => new Dictionary<string, string?>(StringComparer.Ordinal));
        var rootsByTemplateId = adoptions.GroupBy(adoption => adoption.SourceTemplateId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
        var rootsByLocalRecord = adoptions.GroupBy(adoption => (adoption.LocalEntityType, adoption.LocalEntityId))
            .ToDictionary(group => group.Key, group => group.Select(adoption => adoption.Id).ToArray());
        var adoptionsById = adoptions.ToDictionary(adoption => adoption.Id);

        await ReadCategoryFieldsAsync(adoptions, fieldsByAdoption, rootsByLocalRecord, cancellationToken);
        await ReadIngredientFieldsAsync(adoptions, fieldsByAdoption, rootsByLocalRecord, cancellationToken);
        var existingBundleRoots = await ReadProductFieldsAsync(
            adoptions, fieldsByAdoption, rootsByLocalRecord, adoptionsById, cancellationToken);
        await ReadOptionSetFieldsAsync(adoptions, fieldsByAdoption, rootsByLocalRecord, cancellationToken);
        await ReadBundleSectionFieldsAsync(
            adoptions, entryMappings, fieldsByAdoption, rootsByTemplateId, existingBundleRoots, cancellationToken);

        return fieldsByAdoption;
    }
}
