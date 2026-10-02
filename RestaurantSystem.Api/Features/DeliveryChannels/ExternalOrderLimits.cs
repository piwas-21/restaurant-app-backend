namespace RestaurantSystem.Api.Features.DeliveryChannels;

internal static class ExternalOrderLimits
{
    // Storage precision is numeric(10,2). Limits reject rather than truncate source evidence.
    internal const decimal MaxMoney = 99_999_999.99m;
    internal const int MaxItems = 200;
    internal const int MaxQuantity = 100;
    internal const int ItemNameLength = 100;
    internal const int VariationNameLength = 50;
    internal const int ItemInstructionsLength = 500;
    internal const int CatalogueDescriptionLength = 1000;
    // Both supported binding currencies (EUR and CHF) have two decimal minor units.
    internal const decimal MinorUnitsPerWholeUnit = 100m;
    internal const int RequestBytes = 256 * 1024;
}
