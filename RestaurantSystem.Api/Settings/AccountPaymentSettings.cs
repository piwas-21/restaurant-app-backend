using System.ComponentModel.DataAnnotations;

namespace RestaurantSystem.Api.Settings;

public sealed class AccountPaymentSettings
{
    public const string SectionName = "AccountPayments";

    [Range(1, 30)]
    public int QuoteLifetimeMinutes { get; set; } = 5;

    [Range(1, 60)]
    public int ReservationLifetimeMinutes { get; set; } = 10;

    [Range(1, 1000)]
    public int MaximumSelectedUnits { get; set; } = 250;

    [Range(1, 5000)]
    public int MaximumScopeSegments { get; set; } = 2000;

    [Range(1, 1000)]
    public int MaximumEqualShares { get; set; } = 100;

    [Range(1, 1000)]
    public int MaximumActiveAttemptSummaries { get; set; } = 100;
}
