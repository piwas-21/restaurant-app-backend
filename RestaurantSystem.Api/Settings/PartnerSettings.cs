namespace RestaurantSystem.Api.Settings;

/// <summary>Runtime attribution; empty RuntimeUrl retains legacy environment configuration.</summary>
public class PartnerSettings
{
    public const string SectionName = "Partner";
    public string Name { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string RuntimeUrl { get; set; } = string.Empty;
    public string TenantSlug { get; set; } = string.Empty;
    public int RefreshSeconds { get; set; } = 60;
    public int MaxStaleSeconds { get; set; } = 300;
    public int RequestTimeoutSeconds { get; set; } = 3;
}
