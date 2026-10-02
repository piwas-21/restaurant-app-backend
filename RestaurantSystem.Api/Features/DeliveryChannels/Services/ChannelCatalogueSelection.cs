using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.DeliveryChannels.Dtos;

namespace RestaurantSystem.Api.Features.DeliveryChannels.Services;

internal static class ChannelCatalogueSelection
{
    internal static void Require(List<ChannelAvailabilitySelection>? items)
    {
        if (items is null || items.Count is < 1 or > ExternalOrderLimits.MaxItems
            || items.Any(item => item is null || item.ProductId == Guid.Empty || item.VariationId == Guid.Empty)
            || items.Distinct().Count() != items.Count)
            throw new BadRequestException("Select 1–200 distinct product/variation identities for the catalogue.");
    }
}
