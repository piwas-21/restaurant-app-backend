namespace RestaurantSystem.Domain.Entities;

public partial class Order
{
    /// <summary>Tenant-local calendar day used to allocate the shared short order number.</summary>
    public DateOnly BusinessDate { get; set; }

    /// <summary>Numeric order number within <see cref="BusinessDate"/>.</summary>
    public long DailyOrderNumber { get; set; }

    /// <summary>The accepted preparation duration, or the proposed duration while awaiting approval.</summary>
    public int? EffectivePreparationMinutes { get; set; }

    /// <summary>UTC instant the accepted order is expected to be ready; null while awaiting acceptance.</summary>
    public DateTime? ExpectedReadyAt { get; set; }
}
