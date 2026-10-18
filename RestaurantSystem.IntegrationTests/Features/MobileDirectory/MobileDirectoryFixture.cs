using Sofra.MobileService.Directory;

namespace RestaurantSystem.IntegrationTests.Features.MobileDirectory;

internal static class MobileDirectoryFixture
{
    internal static readonly Guid AlphaId = Guid.Parse("11111111-1111-4111-8111-111111111111");
    internal static readonly Guid BetaId = Guid.Parse("22222222-2222-4222-8222-222222222222");
    internal static PublicTenantDto Alpha => new()
    {
        Id = AlphaId,
        Slug = "alpha",
        Name = "Alpha",
        ApiOrigin = "https://alpha.example",
        WebOrigin = "https://alpha.example",
        ImageOrigin = "https://images.example",
        Status = "active",
        ContractVersions = [1]
    };
    internal static PublicTenantDto Beta => Alpha with
    {
        Id = BetaId,
        Slug = "beta",
        Name = "Beta",
        ApiOrigin = "https://beta.example",
        Status = "read-only"
    };
    internal static MobileDirectorySettings Settings(IReadOnlyList<PublicTenantDto>? tenants = null,
        string environment = "staging", int lifetime = 3600, string revision = "reviewed-1") => new()
        {
            Environment = environment,
            Revision = revision,
            LifetimeSeconds = lifetime,
            Tenants = tenants ?? [Alpha, Beta]
        };
}
