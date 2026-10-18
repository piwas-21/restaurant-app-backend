namespace RestaurantSystem.Api.Features.Tenant.Dtos;

/// <summary>Versioned public native contract. Null currency means unknown, never a guessed currency.</summary>
public sealed record CustomerMobileBootstrapDto(
    int SchemaVersion,
    Guid TenantId,
    string? Currency,
    string Locale,
    string Timezone,
    string Lifecycle,
    string Compatibility,
    IReadOnlyList<string> Modules,
    bool ModulesEnforced,
    IReadOnlyDictionary<string, bool> Flags,
    IReadOnlyDictionary<string, string> Capabilities);
