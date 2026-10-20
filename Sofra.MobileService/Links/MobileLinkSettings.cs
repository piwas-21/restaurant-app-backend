namespace Sofra.MobileService.Links;

public sealed class MobileLinkSettings
{
    public const string SectionName = "MobileLinks";
    public bool Enabled { get; init; }
    public string Origin { get; init; } = string.Empty;
}
