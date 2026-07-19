namespace RestaurantSystem.Domain.Common.Enums;

/// <summary>Which physical printer a device receipt was routed to. Mirrors the printer-app's
/// per-target print outcomes (cashier / front-kitchen / back-kitchen).</summary>
public enum DevicePrintTarget
{
    Cashier = 1,
    FrontKitchen = 2,
    BackKitchen = 3
}
