using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.Catalogue.Services;

internal sealed partial class CatalogueRevisionLocalTextReader
{
    private async Task ReadCategoryFieldsAsync(
        IReadOnlyCollection<CatalogueTemplateAdoption> adoptions,
        Dictionary<Guid, Dictionary<string, string?>> fieldsByAdoption,
        Dictionary<(string LocalEntityType, Guid LocalEntityId), Guid[]> rootsByLocalRecord,
        CancellationToken cancellationToken)
    {
        var categoryIds = LocalIds(adoptions, "Category");
        if (categoryIds.Length == 0) return;

        var categories = await context.Categories.AsNoTracking()
            .Where(value => categoryIds.Contains(value.Id)).ToListAsync(cancellationToken);
        foreach (var category in categories)
        {
            foreach (var adoptionId in RootsFor(rootsByLocalRecord, "Category", category.Id))
            {
                var fields = fieldsByAdoption[adoptionId];
                fields["name"] = category.Name;
                fields["description"] = category.Description;
            }
        }
    }

    private async Task ReadIngredientFieldsAsync(
        IReadOnlyCollection<CatalogueTemplateAdoption> adoptions,
        Dictionary<Guid, Dictionary<string, string?>> fieldsByAdoption,
        Dictionary<(string LocalEntityType, Guid LocalEntityId), Guid[]> rootsByLocalRecord,
        CancellationToken cancellationToken)
    {
        var ingredientIds = LocalIds(adoptions, "GlobalIngredient");
        if (ingredientIds.Length == 0) return;

        var ingredients = await context.GlobalIngredients.AsNoTracking().Include(value => value.Translations)
            .Where(value => ingredientIds.Contains(value.Id)).ToListAsync(cancellationToken);
        foreach (var ingredient in ingredients)
        {
            foreach (var adoptionId in RootsFor(rootsByLocalRecord, "GlobalIngredient", ingredient.Id))
            {
                var fields = fieldsByAdoption[adoptionId];
                fields["name"] = ingredient.DefaultName;
                foreach (var translation in ingredient.Translations)
                {
                    fields[$"translations[{translation.LanguageCode}].name"] = translation.Name;
                }
            }
        }
    }

    private async Task<HashSet<Guid>> ReadProductFieldsAsync(
        IReadOnlyCollection<CatalogueTemplateAdoption> adoptions,
        Dictionary<Guid, Dictionary<string, string?>> fieldsByAdoption,
        Dictionary<(string LocalEntityType, Guid LocalEntityId), Guid[]> rootsByLocalRecord,
        Dictionary<Guid, CatalogueTemplateAdoption> adoptionsById,
        CancellationToken cancellationToken)
    {
        var existingBundleRoots = new HashSet<Guid>();
        var productIds = LocalIds(adoptions, "Product", "MenuBundle");
        if (productIds.Length == 0) return existingBundleRoots;

        var products = await context.Products.AsNoTracking().Include(value => value.Descriptions)
            .Where(value => productIds.Contains(value.Id)).ToListAsync(cancellationToken);
        foreach (var product in products)
        {
            var adoptionIds = RootsFor(rootsByLocalRecord, "Product", product.Id)
                .Concat(RootsFor(rootsByLocalRecord, "MenuBundle", product.Id));
            foreach (var adoptionId in adoptionIds)
            {
                PopulateProductFields(fieldsByAdoption[adoptionId], product);
                if (adoptionsById[adoptionId].LocalEntityType == "MenuBundle")
                {
                    existingBundleRoots.Add(adoptionId);
                }
            }
        }

        return existingBundleRoots;
    }

    private static void PopulateProductFields(
        Dictionary<string, string?> fields,
        Product product)
    {
        fields["name"] = product.Name;
        fields["description"] = product.Description;
        foreach (var translation in product.Descriptions)
        {
            fields[$"translations[{translation.Lang}].name"] = translation.Name;
            fields[$"translations[{translation.Lang}].description"] =
                string.IsNullOrEmpty(translation.Description) ? null : translation.Description;
        }
    }

    private async Task ReadOptionSetFieldsAsync(
        IReadOnlyCollection<CatalogueTemplateAdoption> adoptions,
        Dictionary<Guid, Dictionary<string, string?>> fieldsByAdoption,
        Dictionary<(string LocalEntityType, Guid LocalEntityId), Guid[]> rootsByLocalRecord,
        CancellationToken cancellationToken)
    {
        var optionSetIds = LocalIds(adoptions, "OptionSet");
        if (optionSetIds.Length == 0) return;

        var optionSets = await context.OptionSets.AsNoTracking().Include(value => value.Translations)
            .Where(value => optionSetIds.Contains(value.Id)).ToListAsync(cancellationToken);
        foreach (var optionSet in optionSets)
        {
            foreach (var adoptionId in RootsFor(rootsByLocalRecord, "OptionSet", optionSet.Id))
            {
                var fields = fieldsByAdoption[adoptionId];
                fields["name"] = optionSet.Name;
                foreach (var translation in optionSet.Translations)
                {
                    fields[$"translations[{translation.LanguageCode}].name"] = translation.Name;
                }
            }
        }
    }

    private async Task ReadBundleSectionFieldsAsync(
        IReadOnlyCollection<CatalogueTemplateAdoption> adoptions,
        IReadOnlyCollection<CatalogueTemplateAdoption> entryMappings,
        Dictionary<Guid, Dictionary<string, string?>> fieldsByAdoption,
        Dictionary<string, CatalogueTemplateAdoption[]> rootsByTemplateId,
        HashSet<Guid> existingBundleRoots,
        CancellationToken cancellationToken)
    {
        var bundleTemplateIds = adoptions.Where(adoption =>
                CatalogueRevisionTemplateTypes.ForEntity(adoption.LocalEntityType) == "bundle")
            .Select(adoption => adoption.SourceTemplateId).ToHashSet(StringComparer.Ordinal);
        var sectionMappings = entryMappings.Where(mapping => mapping.LocalEntityType == "MenuSection" &&
                mapping.SourceEntryId is not null && bundleTemplateIds.Contains(mapping.SourceTemplateId))
            .ToArray();
        var sectionIds = sectionMappings.Select(mapping => mapping.LocalEntityId).Distinct().ToArray();
        if (sectionIds.Length == 0) return;

        var sections = await context.MenuSections.AsNoTracking()
            .Include(value => value.Translations)
            .Include(value => value.MenuDefinition)
            .Where(value => sectionIds.Contains(value.Id)).ToListAsync(cancellationToken);
        var sectionsById = sections.ToDictionary(section => section.Id);
        foreach (var mapping in sectionMappings)
        {
            if (!sectionsById.TryGetValue(mapping.LocalEntityId, out var section) ||
                !rootsByTemplateId.TryGetValue(mapping.SourceTemplateId, out var rootAdoptions))
            {
                continue;
            }

            foreach (var adoption in rootAdoptions.Where(root =>
                         existingBundleRoots.Contains(root.Id) &&
                         root.LocalEntityId == section.MenuDefinition.ProductId))
            {
                PopulateSectionFields(fieldsByAdoption[adoption.Id], section, mapping.SourceEntryId!);
            }
        }
    }

    private static void PopulateSectionFields(
        Dictionary<string, string?> fields,
        MenuSection section,
        string sourceEntryId)
    {
        var fieldPrefix = $"sections[{sourceEntryId}]";
        fields[$"{fieldPrefix}.name"] = section.Name;
        foreach (var translation in section.Translations)
        {
            fields[$"{fieldPrefix}.translations[{translation.LanguageCode}].name"] = translation.Name;
        }
    }

    private static Guid[] LocalIds(
        IEnumerable<CatalogueTemplateAdoption> adoptions,
        params string[] localEntityTypes) => adoptions
        .Where(adoption => localEntityTypes.Contains(adoption.LocalEntityType, StringComparer.Ordinal))
        .Select(adoption => adoption.LocalEntityId)
        .Distinct()
        .ToArray();

    private static Guid[] RootsFor(
        Dictionary<(string LocalEntityType, Guid LocalEntityId), Guid[]> rootsByLocalRecord,
        string localEntityType,
        Guid localEntityId) => rootsByLocalRecord.GetValueOrDefault((localEntityType, localEntityId), []);
}
