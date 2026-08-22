using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Catalog;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.Basket.Services;

/// <summary>
/// Server-side enforcement of <see cref="Product.HideBaseProduct"/> on the add-to-basket path:
/// a product that does not offer its base row may only be added WITH a variation.
/// </summary>
/// <remarks>
/// Hiding the radio in one React component is presentation, not enforcement — a stale tab, the
/// waiter/POS screen (which expresses "no variation" by de-selecting) or a crafted payload all
/// still post <c>productVariationId: null</c>. Static and DI-free for the same reason as its
/// neighbour <see cref="BasketChannelGuard"/>: the decision is a pure function of the product and
/// the requested variation, both of which the caller has already loaded.
/// </remarks>
public static class BasketBaseProductGuard
{
    /// <summary>
    /// Throws <see cref="BadRequestException"/> when <paramref name="product"/> hides its base row
    /// and no variation was chosen. Permits the add when the product has no ACTIVE variation left —
    /// see <see cref="BaseProductVisibility"/> for why the flag degrades there.
    /// </summary>
    /// <param name="variation">The resolved variation, or <c>null</c> for "the base product".</param>
    public static void EnsureVariationChosen(Product product, ProductVariation? variation)
    {
        if (variation is not null || !BaseProductVisibility.IsBaseHidden(product))
        {
            return;
        }

        throw new BadRequestException(
            $"{product.Name} must be ordered with one of its options.",
            ErrorCodes.VariationRequired);
    }
}
