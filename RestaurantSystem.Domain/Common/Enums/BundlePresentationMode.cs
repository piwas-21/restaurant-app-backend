namespace RestaurantSystem.Domain.Common.Enums;

/// <summary>
/// How the public catalogue presents menu bundles. The legacy value preserves the existing
/// separate-bundle surface; category offers groups linked alternatives into one commercial card.
/// </summary>
public enum BundlePresentationMode
{
    LegacySeparate,
    CategoryOffers
}
