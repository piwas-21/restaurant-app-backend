namespace RestaurantSystem.Channels.Api;

internal static class TenantCatalogueLimits
{
    // Mirrors the independently deployed tenant snapshot and existing order storage contract.
    internal const int SelectionCount = 200;
    internal const int RevisionLength = 64;
    internal const int ItemNameLength = 100;
    internal const int VariationNameLength = 50;
    internal const int DescriptionLength = 1000;
    internal const decimal MaximumVatPercentage = 100m;
}
