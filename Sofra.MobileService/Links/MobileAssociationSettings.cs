namespace Sofra.MobileService.Links;

public sealed class MobileAssociationSettings
{
    public const string SectionName = "MobileAssociations";
    public bool Enabled { get; init; }
    public string AppleApplicationId { get; init; } = string.Empty;
    public string AndroidPackage { get; init; } = string.Empty;
    public IReadOnlyList<string> AndroidSha256Fingerprints { get; init; } = [];
}
