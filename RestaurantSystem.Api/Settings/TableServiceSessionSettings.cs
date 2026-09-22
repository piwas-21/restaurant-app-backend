using System.ComponentModel.DataAnnotations;

namespace RestaurantSystem.Api.Settings;

public sealed class TableServiceSessionSettings
{
    public const string SectionName = "TableServiceSessions";

    [Range(typeof(decimal), "0", "1")]
    public decimal PaymentTolerance { get; set; } = 0.01m;

    [Range(1, 14)]
    public int FloorReservationLookAheadDays { get; set; } = 2;

    [Range(1, 1000)]
    public int PendingPaymentHandoffQueueLimit { get; set; } = 200;
}
