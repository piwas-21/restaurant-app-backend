using System.ComponentModel.DataAnnotations;

namespace RestaurantSystem.Api.Settings;

/// <summary>Configuration for the operational queue's protected synchronization cursors.</summary>
public sealed class OperationalQueueSyncOptions
{
    public const string SectionName = "OperationalQueueSync";
    public const int DefaultCursorLifetimeMinutes = 15;

    [Range(256, 16384)]
    public int MaxCursorLength { get; set; } = 4096;

    [Range(1, 500)]
    public int MaxPageSize { get; set; } = 100;

    /// <summary>
    /// Per-tenant discriminator included in every cursor. Provisioning should set this to the
    /// tenant id; the empty value is retained for the legacy single-tenant install.
    /// </summary>
    public string TenantKey { get; set; } = string.Empty;

    /// <summary>How long a cursor may be replayed before the server rejects it.</summary>
    public TimeSpan CursorLifetime { get; set; } =
        TimeSpan.FromMinutes(DefaultCursorLifetimeMinutes);
}
