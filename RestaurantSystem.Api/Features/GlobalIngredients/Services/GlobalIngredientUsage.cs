using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.GlobalIngredients.Services;

/// <summary>
/// "used on N items" — the reverse link from a library row to the products that copied it (plan
/// S3, and the <c>USAGE</c> column of the approved picker screen).
///
/// <para>
/// It is counted for the WHOLE page in one aggregate query. The picker reads the entire ~650-row
/// catalog in one response (S2), so a per-row count would be 650 round trips per modal open — the
/// N+1 this class exists to make impossible, and what <c>UsageCount_IsOneAggregateQuery_NotOnePerRow</c>
/// pins.
/// </para>
///
/// <para>
/// It counts DISTINCT products, not ingredient rows: nothing stops one product carrying two
/// ingredients copied from the same library row, and "used on 2 items" would then be a lie about
/// one item. It counts through <c>Products</c> rather than <c>ProductIngredients</c> so that the
/// soft-delete query filter on the product applies — a deleted product does not use anything.
/// An INACTIVE product does count: the link is real, and archiving the row still affects it.
/// </para>
/// </summary>
internal static class GlobalIngredientUsage
{
    /// <summary>
    /// Products per library row, for every linked row (<paramref name="ids"/> null) or for just the
    /// rows a page is about. Rows with no link at all are absent — read it through
    /// <see cref="CountFor"/>, which reports those as 0.
    /// </summary>
    public static async Task<IReadOnlyDictionary<Guid, int>> CountByIngredientAsync(
        ApplicationDbContext context,
        IReadOnlyCollection<Guid>? ids,
        CancellationToken cancellationToken)
    {
        if (ids is { Count: 0 })
        {
            return new Dictionary<Guid, int>();
        }

        var ingredients = context.Products.SelectMany(p => p.DetailedIngredients);

        // The null test lives INSIDE each lambda, and the key stays nullable all the way to the
        // pattern match below, so that nothing here needs a null-forgiving `!` (Sonar S8970). The
        // shorter `i.GlobalIngredientId!.Value` is not a style choice that can simply be deleted —
        // `.Value` on its own is CS8629, and this project builds warnings-as-errors. Flow analysis
        // does carry within one lambda body, which is why this form compiles clean.
        var query = ids is null
            ? ingredients.Where(i => i.GlobalIngredientId != null)
            : ingredients.Where(i => i.GlobalIngredientId != null && ids.Contains(i.GlobalIngredientId.Value));

        var counts = await query
            .GroupBy(i => i.GlobalIngredientId)
            .Select(group => new
            {
                GlobalIngredientId = group.Key,
                Products = group.Select(i => i.ProductId).Distinct().Count(),
            })
            .ToListAsync(cancellationToken);

        var byIngredient = new Dictionary<Guid, int>(counts.Count);
        foreach (var entry in counts)
        {
            if (entry.GlobalIngredientId is { } globalIngredientId)
            {
                byIngredient[globalIngredientId] = entry.Products;
            }
        }

        return byIngredient;
    }

    /// <summary>The count for one row, including the 0 that the aggregate omits.</summary>
    public static int CountFor(IReadOnlyDictionary<Guid, int> counts, Guid globalIngredientId) =>
        counts.TryGetValue(globalIngredientId, out var count) ? count : 0;

    /// <summary>The same count for a single row, for a handler that has only one to answer about.</summary>
    public static async Task<int> CountForAsync(
        ApplicationDbContext context,
        Guid globalIngredientId,
        CancellationToken cancellationToken) =>
        await context.Products
            .SelectMany(p => p.DetailedIngredients)
            .Where(i => i.GlobalIngredientId == globalIngredientId)
            .Select(i => i.ProductId)
            .Distinct()
            .CountAsync(cancellationToken);
}
