using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Catalogue.Services;

internal sealed class CatalogueRevisionLocalTextReader(ApplicationDbContext context)
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

        var categoryIds = LocalIds("Category");
        if (categoryIds.Length > 0)
        {
            var categories = await context.Categories.AsNoTracking()
                .Where(value => categoryIds.Contains(value.Id)).ToListAsync(cancellationToken);
            foreach (var category in categories)
            {
                foreach (var adoptionId in RootsFor("Category", category.Id))
                {
                    var fields = fieldsByAdoption[adoptionId];
                    fields["name"] = category.Name;
                    fields["description"] = category.Description;
                }
            }
        }

        var ingredientIds = LocalIds("GlobalIngredient");
        if (ingredientIds.Length > 0)
        {
            var ingredients = await context.GlobalIngredients.AsNoTracking().Include(value => value.Translations)
                .Where(value => ingredientIds.Contains(value.Id)).ToListAsync(cancellationToken);
            foreach (var ingredient in ingredients)
            {
                foreach (var adoptionId in RootsFor("GlobalIngredient", ingredient.Id))
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

        var productIds = LocalIds("Product", "MenuBundle");
        var existingBundleRoots = new HashSet<Guid>();
        if (productIds.Length > 0)
        {
            var products = await context.Products.AsNoTracking().Include(value => value.Descriptions)
                .Where(value => productIds.Contains(value.Id)).ToListAsync(cancellationToken);
            foreach (var product in products)
            {
                foreach (var adoptionId in RootsFor("Product", product.Id)
                             .Concat(RootsFor("MenuBundle", product.Id)))
                {
                    var fields = fieldsByAdoption[adoptionId];
                    fields["name"] = product.Name;
                    fields["description"] = product.Description;
                    foreach (var translation in product.Descriptions)
                    {
                        fields[$"translations[{translation.Lang}].name"] = translation.Name;
                        fields[$"translations[{translation.Lang}].description"] =
                            string.IsNullOrEmpty(translation.Description) ? null : translation.Description;
                    }

                    if (adoptionsById[adoptionId].LocalEntityType == "MenuBundle")
                    {
                        existingBundleRoots.Add(adoptionId);
                    }
                }
            }
        }

        var optionSetIds = LocalIds("OptionSet");
        if (optionSetIds.Length > 0)
        {
            var optionSets = await context.OptionSets.AsNoTracking().Include(value => value.Translations)
                .Where(value => optionSetIds.Contains(value.Id)).ToListAsync(cancellationToken);
            foreach (var optionSet in optionSets)
            {
                foreach (var adoptionId in RootsFor("OptionSet", optionSet.Id))
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

        var bundleTemplateIds = adoptions.Where(adoption =>
                CatalogueRevisionTemplateTypes.ForEntity(adoption.LocalEntityType) == "bundle")
            .Select(adoption => adoption.SourceTemplateId).ToHashSet(StringComparer.Ordinal);
        var sectionMappings = entryMappings.Where(mapping => mapping.LocalEntityType == "MenuSection" &&
                mapping.SourceEntryId is not null && bundleTemplateIds.Contains(mapping.SourceTemplateId))
            .ToArray();
        var sectionIds = sectionMappings.Select(mapping => mapping.LocalEntityId).Distinct().ToArray();
        if (sectionIds.Length > 0)
        {
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

                var fieldPrefix = $"sections[{mapping.SourceEntryId}]";
                foreach (var adoption in rootAdoptions.Where(root =>
                             existingBundleRoots.Contains(root.Id) &&
                             root.LocalEntityId == section.MenuDefinition.ProductId))
                {
                    var fields = fieldsByAdoption[adoption.Id];
                    fields[$"{fieldPrefix}.name"] = section.Name;
                    foreach (var translation in section.Translations)
                    {
                        fields[$"{fieldPrefix}.translations[{translation.LanguageCode}].name"] = translation.Name;
                    }
                }
            }
        }

        return fieldsByAdoption;

        Guid[] LocalIds(params string[] localEntityTypes) => adoptions
            .Where(adoption => localEntityTypes.Contains(adoption.LocalEntityType, StringComparer.Ordinal))
            .Select(adoption => adoption.LocalEntityId)
            .Distinct()
            .ToArray();

        IReadOnlyList<Guid> RootsFor(string localEntityType, Guid localEntityId) =>
            rootsByLocalRecord.GetValueOrDefault((localEntityType, localEntityId), []);
    }
}
