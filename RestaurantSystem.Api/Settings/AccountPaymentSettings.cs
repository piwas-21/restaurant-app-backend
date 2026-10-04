using System.ComponentModel.DataAnnotations;

namespace RestaurantSystem.Api.Settings;

public sealed class AccountPaymentSettings
{
    public const string SectionName = "AccountPayments";
    public const int AbsoluteCashRefundHistoryRowLimit = 10_000;

    // Versioned receipt terms: changing the increment requires a new policy and decoder.
    public const long SwissCashFiveRappenV1IncrementMinor = 5;

    [Range(SwissCashFiveRappenV1IncrementMinor, SwissCashFiveRappenV1IncrementMinor)]
    public long SwissCashIncrementMinor { get; set; } = SwissCashFiveRappenV1IncrementMinor;

    [Required, RegularExpression("^chf-cash-5-rappen-v1$")]
    public string SwissCashPolicyVersion { get; set; } = "chf-cash-5-rappen-v1";

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
