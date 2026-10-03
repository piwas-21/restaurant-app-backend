namespace RestaurantSystem.Domain.Common.Enums;

/// <summary>Guest turnover readiness of a configured table, independent of visit occupancy.</summary>
public enum TableReadinessState
{
    NeedsReset = 1,
    ReadyForGuests = 2
}
