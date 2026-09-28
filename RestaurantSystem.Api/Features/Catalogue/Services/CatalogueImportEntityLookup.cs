using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Catalogue.Services;

internal readonly record struct CatalogueLocalEntityKey(string EntityType, Guid Id);

internal static class CatalogueImportEntityLookup
{
    public static async Task<Dictionary<CatalogueLocalEntityKey, string>> LoadNamesAsync(
        ApplicationDbContext context,
        IEnumerable<CatalogueLocalEntityKey> requested,
        CancellationToken cancellationToken)
    {
        var names = new Dictionary<CatalogueLocalEntityKey, string>();
        var keys = requested.Where(key => key.Id != Guid.Empty).Distinct().ToArray();
        var categoryIds = Ids("Category");
        if (categoryIds.Length > 0) Add("Category", await context.Categories
            .Where(row => categoryIds.Contains(row.Id))
            .Select(row => new CatalogueLocalNameRow(row.Id, row.Name)).ToListAsync(cancellationToken));
        var ingredientIds = Ids("GlobalIngredient");
        if (ingredientIds.Length > 0) Add("GlobalIngredient", await context.GlobalIngredients
            .Where(row => ingredientIds.Contains(row.Id))
            .Select(row => new CatalogueLocalNameRow(row.Id, row.DefaultName)).ToListAsync(cancellationToken));
        var productIds = Ids("Product");
        if (productIds.Length > 0) Add("Product", await context.Products
            .Where(row => productIds.Contains(row.Id) && row.Type != ProductType.Menu)
            .Select(row => new CatalogueLocalNameRow(row.Id, row.Name)).ToListAsync(cancellationToken));
        var bundleIds = Ids("MenuBundle");
        if (bundleIds.Length > 0) Add("MenuBundle", await context.Products
            .Where(row => bundleIds.Contains(row.Id) && row.Type == ProductType.Menu)
            .Select(row => new CatalogueLocalNameRow(row.Id, row.Name)).ToListAsync(cancellationToken));
        var optionSetIds = Ids("OptionSet");
        if (optionSetIds.Length > 0) Add("OptionSet", await context.OptionSets
            .Where(row => optionSetIds.Contains(row.Id))
            .Select(row => new CatalogueLocalNameRow(row.Id, row.Name)).ToListAsync(cancellationToken));
        return names;

        Guid[] Ids(string entityType) => keys.Where(key => key.EntityType == entityType).Select(key => key.Id).ToArray();

        void Add(string entityType, IEnumerable<CatalogueLocalNameRow> found)
        {
            foreach (var row in found) names[new CatalogueLocalEntityKey(entityType, row.Id)] = row.Name;
        }
    }

    public static async Task<HashSet<CatalogueLocalEntityKey>> LoadExistingAsync(
        ApplicationDbContext context,
        IEnumerable<CatalogueLocalEntityKey> requested,
        CancellationToken cancellationToken)
    {
        var existing = new HashSet<CatalogueLocalEntityKey>();
        var keys = requested.Where(key => key.Id != Guid.Empty)
            .Distinct()
            .ToArray();

        await AddAsync("Category", context.Categories.Select(row => row.Id));
        await AddAsync("GlobalIngredient", context.GlobalIngredients.Select(row => row.Id));
        await AddAsync("Product", context.Products.Where(row => row.Type != ProductType.Menu).Select(row => row.Id));
        await AddAsync("MenuBundle", context.Products.Where(row => row.Type == ProductType.Menu).Select(row => row.Id));
        await AddAsync("OptionSet", context.OptionSets.Select(row => row.Id));
        return existing;

        async Task AddAsync(string entityType, IQueryable<Guid> query)
        {
            var ids = keys.Where(key => key.EntityType == entityType)
                .Select(key => key.Id)
                .ToArray();
            if (ids.Length == 0)
            {
                return;
            }

            var found = await query.Where(id => ids.Contains(id)).ToListAsync(cancellationToken);
            foreach (var id in found)
            {
                existing.Add(new CatalogueLocalEntityKey(entityType, id));
            }
        }
    }
}

internal sealed record CatalogueLocalNameRow(Guid Id, string Name);
