using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.Catalog;
using RestaurantSystem.Domain.Common;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.Basket.Services;

/// <summary>
/// Server-side enforcement of per-order-type availability on the add-to-basket path. Client-side
/// dimming is presentation only — a stale tab or a tampered payload must not be able to build a
/// basket the kitchen cannot fulfil.
/// </summary>
/// <remarks>
/// Static (no DI) to match its <see cref="OrderTypeAvailability"/> collaborator: the decision is a
/// pure function of the product and the basket's channel, and the caller has already loaded both.
/// </remarks>
public static class BasketChannelGuard
{
    /// <summary>
    /// Throws <see cref="BadRequestException"/> when <paramref name="product"/> cannot be ordered on
    /// the basket's channel.
    /// </summary>
    /// <param name="basketOrderType">
    /// The basket's channel, or <c>null</c> when the guest has not chosen one — the dominant browse
    /// state, which is deliberately permissive so pre-pick adds keep working.
    /// </param>
    /// <remarks>
    /// The message names the channels the item IS available on, because the client turns it into
    /// "Dürüm is takeaway &amp; delivery only" plus a one-tap switch. A bare "not available" would
    /// destroy the point of the feature.
    /// <para>
    /// Requires <c>ProductCategories → Category</c> to be loaded for inheritance to resolve; an
    /// unloaded collection reads as unrestricted (permissive), never as blocked.
    /// </para>
    /// </remarks>
    public static void EnsureOrderable(Product product, OrderType? basketOrderType)
    {
        if (basketOrderType is null)
        {
            return;
        }

        var mask = OrderTypeAvailability.EffectiveMask(product);
        if (OrderChannelMap.Allows(mask, basketOrderType.Value))
        {
            return;
        }

        var allowed = string.Join(", ", OrderChannelMap.ToOrderTypes(mask));
        throw new BadRequestException(
            $"{product.Name} is not available for {basketOrderType}. Available for: {allowed}.");
    }
}
