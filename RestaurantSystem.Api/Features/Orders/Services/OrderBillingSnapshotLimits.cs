namespace RestaurantSystem.Api.Features.Orders.Services;

/// <summary>Hard memory/storage guard for the required one-row-per-unit billing snapshot.</summary>
internal static class OrderBillingSnapshotLimits
{
    internal const int CurrencyMinorDigits = 2;
    internal const int AbsoluteMaximumUnitRows = 10_000;
}
