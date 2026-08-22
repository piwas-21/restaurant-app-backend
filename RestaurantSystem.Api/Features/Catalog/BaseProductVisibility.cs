using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.Catalog;

/// <summary>
/// The single rule for "is the base product (ordering with no variation) hidden?".
/// </summary>
/// <remarks>
/// <see cref="Product.HideBaseProduct"/> is what the admin stored; this is what everybody must
/// ACT on. The difference is the degrade: a product whose variations are all inactive has no
/// orderable option left once the base row is gone, so the flag degrades to <c>false</c> at read
/// time and the item stays orderable rather than going silently dead
/// (BUGS-IMPROVEMENTS-PLAN Track F, F2 risk 4).
/// <para>
/// The degrade lives HERE and not in the wire DTO on purpose: <c>ProductDto</c> feeds the admin
/// editor as well as the guest sheet, and a degraded value round-tripping through that form would
/// silently erase the stored flag the moment every variation happened to be off. The DTO therefore
/// carries the stored bool and each reader — the basket guard here, the sheet on the client —
/// applies this same rule against the active variations it already has.
/// </para>
/// <para>
/// Requires <c>Variations</c> to be loaded. An unloaded collection reads as "no active variation"
/// and therefore as NOT hidden — permissive, never blocking, which matches the guard's direction.
/// </para>
/// </remarks>
public static class BaseProductVisibility
{
    /// <summary>True when the guest must choose a variation because the base row is not offered.</summary>
    public static bool IsBaseHidden(Product product) =>
        product.HideBaseProduct && product.Variations.Any(v => v.IsActive);
}
