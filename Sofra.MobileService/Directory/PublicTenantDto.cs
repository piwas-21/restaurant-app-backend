namespace Sofra.MobileService.Directory;

public sealed record PublicTenantDto
{
    public Guid Id { get; init; }
    public string Slug { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string ApiOrigin { get; init; } = string.Empty;
    public string WebOrigin { get; init; } = string.Empty;
    public string ImageOrigin { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public IReadOnlyList<int> ContractVersions { get; init; } = [];
}
