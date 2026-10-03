using System.ComponentModel.DataAnnotations;

namespace RestaurantSystem.Api.Settings;

/// <summary>Set settlement currency after provider verification; cross-currency contributions remain unavailable.</summary>
public sealed class AccountOnlineContributionSettings
{
    public const string SectionName = "AccountOnlineContribution";
    public const long ProviderMaximumAmountMinor = 99_999_999;
    [RegularExpression("^(CHF|EUR|USD|GBP|AED)$")]
    public string? SettlementCurrency { get; set; }
    [Range(1, ProviderMaximumAmountMinor)]
    public long MinimumAmountMinor { get; set; } = 50;
    [Range(1, ProviderMaximumAmountMinor)]
    public long MaximumAmountMinor { get; set; } = ProviderMaximumAmountMinor;

    public bool HasValidLimits() => MaximumAmountMinor <= ProviderMaximumAmountMinor
        && MaximumAmountMinor >= MinimumAmountMinor
        && MinimumAmountMinor >= (SettlementCurrency switch
        {
            null => 1,
            "CHF" or "EUR" or "USD" => 50,
            "GBP" => 30,
            "AED" => 200,
            _ => long.MaxValue
        });
}
