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
/// <c>GetProductByIdQuery</c>, and — since the G7 / §9.2 slice — <c>FeaturedSpecialDto</c> and
/// <c>SpecialProductDto</c>. NOT YET WIRED: <c>MenuBundleDto</c> (bundle list/detail), which still
/// renders blocked items as fully orderable and additionally cannot yet STORE a mask — no bundle
/// command accepts <c>AvailableOrderTypes</c>. Tracked as follow-up.
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
    /// A product with no primary category resolves to <c>null</c> (unrestricted). That is the
    /// deliberate permissive fallback: silently blocking sales is worse than allowing them. It is a
    /// DATA GAP though, not a normal state — <see cref="HasResolvableInheritance"/> lets the admin
    /// surface flag it, because <c>ProductCategory.IsPrimary</c> can be silently re-pointed by an
    /// unrelated product save.
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
    /// False when a product inherits (no own mask) but has no primary category to inherit FROM — the
    /// data gap described on <see cref="EffectiveMask"/>. Admin surfaces warn on this.
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

    private static Category? PrimaryCategoryOf(Product product) =>
        product.ProductCategories.FirstOrDefault(pc => pc.IsPrimary)?.Category;
}
