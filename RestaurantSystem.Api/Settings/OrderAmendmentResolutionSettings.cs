using System.ComponentModel.DataAnnotations;

namespace RestaurantSystem.Api.Settings;

public sealed class OrderAmendmentResolutionSettings
{
    public const string SectionName = "OrderAmendmentResolution";

    [Range(1, 10)]
    public int AmendmentQuoteLifetimeMinutes { get; set; } = 5;

    [Range(1, 10)]
    public int FinancialResolutionQuoteLifetimeMinutes { get; set; } = 2;

    [Range(1, 100)]
    public int ProviderRefundPageSize { get; set; } = 100;

    [Range(1, 10)]
    public int MaximumProviderRefundPages { get; set; } = 10;

    [Range(1, 23)]
    public int ProviderIdempotencySafetyWindowHours { get; set; } = 23;
}
