using RestaurantSystem.Api.Features.Catalog.Dtos;
using RestaurantSystem.Domain.Common;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.Catalog;

/// <summary>
/// The single resolver for per-order-type catalog availability. Static to match its sibling
/// <see cref="ProductDtoMapper"/>.
/// </summary>
/// <remarks>
/// Exists because the field would otherwise be hand-rolled in every catalog projection. Callers must
/// have loaded <c>ProductCategories → Category</c> for inheritance to resolve; a product with that
/// collection unloaded reads as unrestricted (permissive), never as blocked.
/// <para>
/// WIRED UP: <see cref="ProductDtoMapper"/>, <see cref="ProductSummaryMapper"/>,
/// <c>GetProductByIdQuery</c>, <c>FeaturedSpecialDto</c> and <c>SpecialProductDto</c> (the G7 slice),
/// and — since §9.2 — <c>MenuBundleDto</c> (bundle list + detail), whose commands now also STORE a
/// mask. NOT WIRED: <c>CategoryProductDto</c>, which carries no availability field at all. Its one
/// remaining producer is <c>CategoryDetailDto.FeaturedProducts</c> (<c>GetCategoryByIdQuery</c>) —
/// and <c>GET /api/Categories/{id}</c> has no consumer in any client repo either, so nothing renders
/// an undimmed item from it today. The other producer, <c>GetCategoryProductsQuery</c>, was DELETED
/// 2026-07-29 (plan §9.16) rather than wired, for that same reason. Do not add availability here
/// speculatively: wire it when something actually reads the endpoint, and load
/// <c>ProductCategories → Category</c> in the same change or the verdict is silently permissive.
/// </para>
/// <para>
/// Every caller must load <c>ProductCategories -&gt; Category</c>. An unloaded collection resolves
/// as UNRESTRICTED, so a missing include is a silently permissive verdict rather than an error.
/// </para>
/// </remarks>
public static class OrderTypeAvailability
{
    /// <summary>
    /// The channel mask actually governing a product: its own override, else its PRIMARY category's.
    /// </summary>
    /// <remarks>
    /// Inheritance is all-or-nothing — the mask is one nullable field, so a product cannot inherit
    /// one channel while overriding another.
    /// <para>
    /// A product with no primary category — or one whose primary category has been soft-deleted
    /// (§9.14) — resolves to <c>null</c> (unrestricted). That is the deliberate permissive fallback:
    /// silently blocking sales is worse than allowing them. It is a DATA GAP though, not a normal
    /// state, because <c>ProductCategory.IsPrimary</c> can be silently re-pointed by an unrelated
    /// product save. <see cref="HasResolvableInheritance"/> exists for an admin surface to flag it;
    /// nothing calls it yet.
    /// </para>
    /// </remarks>
    public static int? EffectiveMask(Product product)
    {
        if (product.AvailableOrderTypes is not null)
        {
            return product.AvailableOrderTypes;
        }

        return PrimaryCategoryOf(product)?.AvailableOrderTypes;
    }

    /// <summary>
    /// False when a product inherits (no own mask) but has no LIVE primary category to inherit FROM —
    /// the data gap described on <see cref="EffectiveMask"/>, which a soft-deleted primary also
    /// produces. Intended for an admin warning; no production caller yet.
    /// </summary>
    public static bool HasResolvableInheritance(Product product) =>
        product.AvailableOrderTypes is not null || PrimaryCategoryOf(product) is not null;

    /// <summary>
    /// Resolve availability for a requested order type. Pass <paramref name="requestedOrderType"/>
    /// as <c>null</c> for the browse-with-no-type-chosen case: the item is always orderable, and
    /// <see cref="ItemAvailabilityDto.AllowedOrderTypes"/> still drives the informational chip.
    /// </summary>
    public static ItemAvailabilityDto Resolve(Product product, OrderType? requestedOrderType)
    {
        var mask = EffectiveMask(product);
        var allowed = OrderChannelMap.ToOrderTypes(mask);
        var inherits = product.AvailableOrderTypes is null;

        // Precedence: unavailable beats wrong-channel, so a guest is never told to switch order
        // type for an item that is switched off on every channel.
        if (!product.IsAvailable)
        {
            return new ItemAvailabilityDto
            {
                CanOrder = false,
                Reason = AvailabilityReason.Unavailable,
                AllowedOrderTypes = allowed,
                InheritsOrderTypes = inherits
            };
        }

        var blockedByChannel = requestedOrderType is not null
            && !OrderChannelMap.Allows(mask, requestedOrderType.Value);

        return new ItemAvailabilityDto
        {
            CanOrder = !blockedByChannel,
            Reason = blockedByChannel ? AvailabilityReason.WrongOrderType : AvailabilityReason.Available,
            AllowedOrderTypes = allowed,
            InheritsOrderTypes = inherits
        };
    }

    /// <summary>
    /// The product's primary category, or <c>null</c> when it has none, when the navigation was not
    /// loaded, or when the category has been SOFT-DELETED.
    /// </summary>
    /// <remarks>
    /// The deleted check is what makes the verdict independent of which query filters ran (§9.14).
    /// <c>Category</c> is a <c>SoftDeleteEntity</c> behind a global filter and <c>ProductCategory</c>
    /// is not, so a product whose primary category is deleted came back with that join row DROPPED on
    /// the ordinary catalog queries (measured: permissive, all three channels) but present and
    /// live-looking on <c>GetProductByIdQuery</c>, whose <c>IgnoreQueryFilters()</c> un-filters the
    /// INCLUDES (measured: blocked). One data state, two answers: the card said yes, the sheet said
    /// no — §9.10's shape with the surfaces swapped.
    /// <para>
    /// Permissive is the correct side. A restriction inherited from a category the admin can no
    /// longer see or edit is an invisible block on sales, and "no primary category" already resolves
    /// permissively by documented design — so this simply makes a deleted primary mean the same thing
    /// as a missing one, which is what it is.
    /// </para>
    /// <para>
    /// The filter is shared with the catalog projections via <see cref="LiveProductCategories"/> so
    /// the two cannot drift; its null-pattern also means this never dereferences a navigation whose
    /// principal was filtered out, without depending on EF's exact treatment of that case.
    /// </para>
    /// </remarks>
    private static Category? PrimaryCategoryOf(Product product) =>
        LiveProductCategories.Of(product).FirstOrDefault(pc => pc.IsPrimary)?.Category;
}
