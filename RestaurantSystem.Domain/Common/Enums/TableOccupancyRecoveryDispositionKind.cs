namespace RestaurantSystem.Domain.Common.Enums;

/// <summary>Audited outcome for an order included in explicit physical-table recovery.</summary>
public enum TableOccupancyRecoveryDispositionKind
{
    CancelledUnsent = 1,
    ArchivedLegacyOccupancy = 2,
    RetainedInPriorVisit = 3
}
