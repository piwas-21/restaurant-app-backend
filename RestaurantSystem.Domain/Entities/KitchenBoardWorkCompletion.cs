using RestaurantSystem.Domain.Common.Base;
using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.Domain.Entities;

/// <summary>Immutable acknowledgement of one initial order or amendment correction on the native kitchen board.</summary>
public class KitchenBoardWorkCompletion : Entity
{
    public Guid OrderId { get; set; }
    public Guid WorkItemId { get; set; }
    public KitchenBoardWorkKind Kind { get; set; }
    public int AcknowledgedOrderVersion { get; set; }
    public long? AccountRevision { get; set; }

    /// <summary>Database sequence used by the board completion change feed.</summary>
    public long Sequence { get; set; }

    public virtual Order Order { get; set; } = null!;
}
