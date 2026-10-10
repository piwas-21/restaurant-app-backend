namespace RestaurantSystem.Domain.Common.Enums;

/// <summary>Declares how a frozen quantity relates to its containing order item.</summary>
public enum QuantityBasis
{
    PerParentUnit,
    LineTotal,
    Unknown
}
