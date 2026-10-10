namespace RestaurantSystem.Domain.Common.Enums;

/// <summary>Whether one child configuration is explicitly shared across all parent units.</summary>
public enum ConfigurationScope
{
    SharedAcrossParentUnits,
    IndependentParentUnits,
    Unknown
}
