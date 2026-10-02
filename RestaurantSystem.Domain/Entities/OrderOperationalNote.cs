using RestaurantSystem.Domain.Common.Base;
using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.Domain.Entities;

/// <summary>
/// An append-only instruction for staff. This is deliberately separate from <see cref="Order.Notes"/>,
/// which is customer-facing order context and can reach guest receipts and email.
/// </summary>
public class OrderOperationalNote : Entity
{
    public Guid OrderId { get; set; }
    public string Text { get; set; } = string.Empty;
    public OrderNoteAudience Audience { get; set; }
    public Guid ClientOperationId { get; set; }

    /// <summary>Optional immutable preparation payload. Legacy text notes leave these null.</summary>
    public Guid? AmendmentId { get; set; }
    public long? AccountRevision { get; set; }
    public DevicePrintTarget? KitchenTarget { get; set; }
    public string? KitchenChangesJson { get; set; }

    public virtual Order Order { get; set; } = null!;
}
