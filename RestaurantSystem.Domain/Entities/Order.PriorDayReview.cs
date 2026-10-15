namespace RestaurantSystem.Domain.Entities;

public partial class Order
{
    /// <summary>When staff dismissed this order from the prior-day unresolved review.</summary>
    public DateTime? PriorDayReviewDismissedAt { get; set; }

    /// <summary>Staff identity that dismissed this order from the prior-day unresolved review.</summary>
    public string? PriorDayReviewDismissedBy { get; set; }
}
