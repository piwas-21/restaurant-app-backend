using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Catalogue.Services;

internal readonly record struct CatalogueLocalEntityKey(string EntityType, Guid Id);

internal static class CatalogueImportEntityLookup
{
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
