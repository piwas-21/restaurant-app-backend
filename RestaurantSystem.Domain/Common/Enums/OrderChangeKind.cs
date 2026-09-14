namespace RestaurantSystem.Domain.Common.Enums;

/// <summary>The kind of operational order change recorded in the durable journal.</summary>
public enum OrderChangeKind
{
    /// <summary>The order currently has a representation in the operational queue.</summary>
    Upsert = 0,

    /// <summary>The order was removed from the queue, normally by a soft delete.</summary>
    Remove = 1,
}
