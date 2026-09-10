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

    public virtual Order Order { get; set; } = null!;
}
