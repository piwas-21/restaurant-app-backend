namespace Sofra.MobileService.Directory;

public sealed class MobileDirectorySettings
{
    public const string SectionName = "MobileDirectory";
    public const int MaximumTenants = 1000;
    public const int MinimumLifetimeSeconds = 60;
    public const int MaximumLifetimeSeconds = 86400;
    public string Environment { get; init; } = string.Empty;
    public string Revision { get; init; } = string.Empty;
    public int LifetimeSeconds { get; init; } = 3600;
    public IReadOnlyList<PublicTenantDto> Tenants { get; init; } = [];
}
