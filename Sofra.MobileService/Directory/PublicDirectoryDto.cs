namespace Sofra.MobileService.Directory;

public sealed record PublicDirectoryDto(
    int SchemaVersion,
    string Revision,
    string Environment,
    DateTime PublishedAt,
    DateTime ExpiresAt,
    IReadOnlyList<PublicTenantDto> Tenants);
