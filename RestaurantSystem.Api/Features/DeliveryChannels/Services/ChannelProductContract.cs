using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.DeliveryChannels.Services;

internal static class ChannelProductContract
{
    // The current wire cannot express bundles or choices. Stock sync must never resume them.
    internal static bool IsUnsupported(Product product) => product.Type == ProductType.Menu
        || product.CustomizationGroups.Any(group => group.IsActive)
        || product.SauceMin > 0 || product.DetailedIngredients.Count > 0;
}
