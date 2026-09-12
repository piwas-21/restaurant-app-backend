namespace RestaurantSystem.Domain.Common.Enums;

/// <summary>Which logical printer destination a device receipt was routed to. Values 1–3 are
/// retained for existing printer-app clients; General and Default are additive destinations for
/// single-kitchen and station fallback jobs.</summary>
public enum DevicePrintTarget
{
    Cashier = 1,
    FrontKitchen = 2,
    BackKitchen = 3,
    General = 4,
    Default = 5
}
