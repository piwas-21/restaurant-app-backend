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

        var query = context.Products
            .SelectMany(p => p.DetailedIngredients)
            .Where(i => i.GlobalIngredientId != null);

        if (ids is not null)
        {
            query = query.Where(i => ids.Contains(i.GlobalIngredientId!.Value));
        }

        var counts = await query
            .GroupBy(i => i.GlobalIngredientId!.Value)
            .Select(group => new
            {
                GlobalIngredientId = group.Key,
                Products = group.Select(i => i.ProductId).Distinct().Count(),
            })
            .ToListAsync(cancellationToken);

        return counts.ToDictionary(entry => entry.GlobalIngredientId, entry => entry.Products);
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
