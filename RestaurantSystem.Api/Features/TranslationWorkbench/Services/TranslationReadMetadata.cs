using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Features.Menus.Dtos;
using RestaurantSystem.Api.Features.Products.Dtos;
using RestaurantSystem.Api.Features.TranslationWorkbench.Dtos;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.TranslationWorkbench.Services;

public static class TranslationReadMetadata
{
    public static async Task ApplyAsync(
        ApplicationDbContext context,
        ProductDto product,
        CancellationToken cancellationToken)
    {
        var ids = new[] { product.Id }
            .Concat(product.Variations.Select(row => row.Id))
            .Concat((product.DetailedIngredients ?? []).Select(row => row.Id.GetValueOrDefault()))
            .Concat(product.MenuDefinition?.Sections.Select(row => row.Id) ?? [])
            .Where(id => id != Guid.Empty).Distinct().ToArray();
        var sources = await LoadAsync(context, ids, cancellationToken);
        product.TranslationMetadata = Find(sources, "product", product.Id);
        foreach (var variation in product.Variations)
        {
            variation.TranslationMetadata = Find(sources, "productVariation", variation.Id);
        }

        foreach (var ingredient in product.DetailedIngredients ?? [])
        {
            if (ingredient.Id is Guid id)
            {
                ingredient.TranslationMetadata = Find(sources, "productIngredient", id);
            }
        }

        foreach (var section in product.MenuDefinition?.Sections ?? [])
        {
            section.TranslationMetadata = Find(sources, "menuSection", section.Id);
        }
    }

    public static async Task ApplyAsync(
        ApplicationDbContext context,
        MenuBundleDto bundle,
        CancellationToken cancellationToken)
    {
        var ids = new[] { bundle.Id }
            .Concat(bundle.MenuDefinition?.Sections.Select(row => row.Id) ?? [])
            .ToArray();
        var sources = await LoadAsync(context, ids, cancellationToken);
        bundle.TranslationMetadata = Find(sources, "product", bundle.Id);
        foreach (var section in bundle.MenuDefinition?.Sections ?? [])
        {
            section.TranslationMetadata = Find(sources, "menuSection", section.Id);
        }
    }

    private static async Task<Dictionary<(string EntityType, Guid EntityId), TranslationOwnerMetadataDto>> LoadAsync(
        ApplicationDbContext context,
        Guid[] ids,
        CancellationToken cancellationToken)
    {
        var rows = await context.TranslationFieldProvenances.AsNoTracking()
            .Where(row => ids.Contains(row.EntityId) && row.Locale == row.SourceLocale &&
                (row.Kind == "tenantSource" || row.Kind == "template"))
            .OrderByDescending(row => row.UpdatedAt)
            .ToListAsync(cancellationToken);
        return rows.GroupBy(row => (row.EntityType, row.EntityId))
            .ToDictionary(group => group.Key, group => new TranslationOwnerMetadataDto
            {
                SourceLocales = group.GroupBy(row => row.FieldKey)
                    .ToDictionary(field => field.Key, field => field.First().SourceLocale,
                        StringComparer.Ordinal)
            });
    }

    private static TranslationOwnerMetadataDto? Find(
        Dictionary<(string EntityType, Guid EntityId), TranslationOwnerMetadataDto> sources,
        string entityType,
        Guid id) => sources.TryGetValue((entityType, id), out var metadata) ? metadata : null;
}
