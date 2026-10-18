using Microsoft.Extensions.Options;

namespace Sofra.MobileService.Directory;

public sealed class GetMobileDirectoryQueryHandler : IGetMobileDirectoryQueryHandler
{
    private const int SchemaVersion = 1;
    private readonly MobileDirectorySettings _settings;
    private readonly IReadOnlyList<PublicTenantDto> _tenants;
    private readonly TimeProvider _clock;

    public GetMobileDirectoryQueryHandler(IOptions<MobileDirectorySettings> settings, TimeProvider clock)
    {
        _settings = settings.Value;
        _clock = clock;
        // Copy all collections once. No caller can mutate another response or live configuration.
        _tenants = Array.AsReadOnly(_settings.Tenants.Select(tenant => tenant with
        {
            ContractVersions = Array.AsReadOnly(tenant.ContractVersions.ToArray())
        }).ToArray());
    }

    public PublicDirectoryDto Handle()
    {
        var publishedAt = _clock.GetUtcNow().UtcDateTime;
        return new PublicDirectoryDto(SchemaVersion, _settings.Revision, _settings.Environment,
            publishedAt, publishedAt.AddSeconds(_settings.LifetimeSeconds), _tenants);
    }
}
