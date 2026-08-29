using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.Basket.Services;

/// <summary>
/// Server-side enforcement of <see cref="Product.IsComponent"/> on the add-to-basket path: a
/// component may be CHOSEN INSIDE a bundle, and may not be a basket line of its own.
/// </summary>
/// <remarks>
/// Excluding components from the catalogue queries hides the card; it is not a rule. The product id
/// is still discoverable — it is returned inside every bundle's <c>MenuDefinition</c>, and
/// <c>GET /api/Products/{id}</c> serves it on purpose so the admin editor can open one — so
/// "nothing links to it" is a fact about our clients, not a guarantee about the endpoint.
/// <para>
/// <b>TOP-LEVEL lines only.</b> This is called from <c>BasketService.AddItemToBasketAsync</c> on the
/// product the REQUEST names. A bundle's chosen options are resolved deeper, inside
/// <c>BasketItemFactory</c>, and never pass through here — which is the entire point: "choose
/// exactly 2 meats out of 6" is a <c>MenuSection</c> with <c>MinSelection = MaxSelection = 2</c>
/// over 6 component products, and each of those 6 must remain choosable there.
/// </para>
/// <para>
/// Static and DI-free for the same reason as its neighbours <see cref="BasketChannelGuard"/> and
/// <see cref="BasketBaseProductGuard"/>: the decision is a pure function of one already-loaded
/// product, reading a stored column with no inheritance and no derived state.
/// </para>
/// </remarks>
public static class BasketComponentGuard
{
    /// <summary>
    /// Throws <see cref="BadRequestException"/> when <paramref name="product"/> is a component.
    /// </summary>
    public static void EnsureNotOrderedAlone(Product product)
    {
        if (!product.IsComponent)
        {
            return;
        }

        throw new BadRequestException(
            $"{product.Name} can only be chosen inside a menu, not ordered on its own.",
            ErrorCodes.ComponentNotOrderable);
    }
}
