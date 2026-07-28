using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.Catalog;

/// <summary>
/// A product's category assignments whose category is actually still there — the single filter every
/// catalog projection applies before reading <c>pc.Category</c>.
/// </summary>
/// <remarks>
/// Exists because <c>Category</c> is a soft-delete entity behind a global query filter while
/// <c>ProductCategory</c> is not, so a join row can outlive its category (§9.14). A caller that uses
/// <c>IgnoreQueryFilters()</c> — <c>GetProductByIdQuery</c> does, which un-filters its INCLUDES —
/// otherwise reports a deleted category as a live assignment and inherits its channel mask, while
/// the ordinary catalog queries report neither. One data state, two answers.
/// <para>
/// On a query whose filters DO run this is a no-op: EF drops the join row along with its category
/// (measured, not assumed — the pre-fix list endpoint dereferenced <c>pc.Category.Name</c> unguarded
/// and returned 200). It is kept as one shared rule anyway, because the guards
/// (<c>BasketChannelGuard</c>, <c>OrderChannelGuard</c>) must not have a verdict that depends on
/// which filters happened to run, and because the null-pattern makes dereferencing a filtered-out
/// principal unreachable without relying on EF's treatment of that case.
/// </para>
/// <para>
/// <b>Not a substitute for the include.</b> A caller that forgets
/// <c>ThenInclude(pc =&gt; pc.Category)</c> now gets an empty category list and a silently permissive
/// verdict instead of a loud <c>NullReferenceException</c> — the repo's most-repeated bug class. The
/// include is still mandatory; this only decides which loaded rows count.
/// </para>
/// <see cref="OrderTypeAvailability"/> resolves inheritance through this same filter.
/// </remarks>
public static class LiveProductCategories
{
    public static IEnumerable<ProductCategory> Of(Product product) =>
        product.ProductCategories.Where(pc => pc.Category is { IsDeleted: false });
}
