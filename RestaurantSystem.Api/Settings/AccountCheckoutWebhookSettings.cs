using System.ComponentModel.DataAnnotations;

namespace RestaurantSystem.Api.Settings;

/// <summary>Optional endpoint-specific Stripe signature and ingress limits.</summary>
public sealed class AccountCheckoutWebhookSettings
{
    public const string SectionName = "AccountCheckoutWebhook";

    public string SigningSecret { get; set; } = string.Empty;

    [Range(1, 1000)]
    public int MaximumRequestsPerMinute { get; set; } = 120;

    [Range(1024, 1048576)]
    public int MaximumPayloadBytes { get; set; } = 262144;

    [Range(128, 4096)]
    public int MaximumSignatureHeaderLength { get; set; } = 1024;

    [Range(60, 600)]
    public int SignatureToleranceSeconds { get; set; } = 300;
}
