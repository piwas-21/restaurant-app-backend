using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.GlobalVariations.Services;

/// <summary>
/// "used on N items" for the variation library — the same shape as <c>GlobalIngredientUsage</c>
/// (S3), for the same two reasons: the count must be over DISTINCT products, and it must cost one
/// aggregate for the whole page rather than one query per row.
///
/// <para>
/// One difference is worth naming: <c>ProductVariation</c> IS soft-deletable, so walking from
/// <c>Products</c> through the navigation applies both filters — a deleted product and a deleted
/// variation are each already excluded, with no predicate of our own.
/// </para>
/// </summary>
internal static class GlobalVariationUsage
{
    public static async Task<IReadOnlyDictionary<Guid, int>> CountByVariationAsync(
        ApplicationDbContext context,
        IReadOnlyCollection<Guid>? ids,
        CancellationToken cancellationToken)
    {
        if (ids is { Count: 0 })
        {
            return new Dictionary<Guid, int>();
        }

        var variations = context.Products.SelectMany(p => p.Variations);

        // The null test lives inside each lambda and the group key stays nullable until the pattern
        // match below, so nothing here needs a null-forgiving `!` (Sonar S8970); `.Value` alone is
        // CS8629 and this project builds warnings-as-errors.
        var query = ids is null
            ? variations.Where(v => v.GlobalVariationId != null)
            : variations.Where(v => v.GlobalVariationId != null && ids.Contains(v.GlobalVariationId.Value));

        var counts = await query
            .GroupBy(v => v.GlobalVariationId)
            .Select(group => new
            {
                GlobalVariationId = group.Key,
                Products = group.Select(v => v.ProductId).Distinct().Count(),
            })
            .ToListAsync(cancellationToken);

        var byVariation = new Dictionary<Guid, int>(counts.Count);
        foreach (var entry in counts)
        {
            if (entry.GlobalVariationId is { } globalVariationId)
            {
                byVariation[globalVariationId] = entry.Products;
            }
        }

        return byVariation;
    }

    public static int CountFor(IReadOnlyDictionary<Guid, int> counts, Guid globalVariationId) =>
        counts.TryGetValue(globalVariationId, out var count) ? count : 0;

    public static async Task<int> CountForAsync(
        ApplicationDbContext context,
        Guid globalVariationId,
        CancellationToken cancellationToken) =>
        await context.Products
            .SelectMany(p => p.Variations)
            .Where(v => v.GlobalVariationId == globalVariationId)
            .Select(v => v.ProductId)
            .Distinct()
            .CountAsync(cancellationToken);
}
