namespace RestaurantSystem.Domain.Common.Enums;

/// <summary>Logical work represented by a printer acknowledgement. A missing value on an
/// acknowledgement is the legacy order receipt contract.</summary>
public enum DevicePrintJobType
{
    Order = 1,
    Update = 2
}
