namespace RestaurantSystem.Api.Features.Orders.Dtos;

public partial record OrderDto
{
    /// <summary>Tenant-local day used for this order's human-facing daily number.</summary>
    public DateOnly BusinessDate { get; set; }

    /// <summary>Numeric short order number shared across guest, cashier, and printer surfaces.</summary>
    public long DailyOrderNumber { get; set; }

    /// <summary>Resolved preparation duration, including a staff override when supplied.</summary>
    public int? EffectivePreparationMinutes { get; set; }

    /// <summary>Expected ready instant in UTC; null until the order is accepted.</summary>
    public DateTime? ExpectedReadyAt { get; set; }

    /// <summary>IANA timezone used to display business dates and the expected-ready instant.</summary>
    public string BusinessTimeZoneId { get; set; } = string.Empty;
}
