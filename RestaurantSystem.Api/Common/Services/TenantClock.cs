using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Infrastructure.Settings;

namespace RestaurantSystem.Api.Common.Services;

/// <summary>
/// <see cref="ITenantClock"/> over <c>Localization:TimeZone</c>.
/// </summary>
/// <remarks>
/// An unknown or unusable zone id logs and falls back to the default rather than throwing: a typo
/// in one tenant's <c>.env</c> must not stop that tenant booting — the same call
/// <c>EmailLanguageResolver</c> makes about an unusable language list. The fallback is the
/// PRODUCT default (<c>Europe/Zurich</c>), not UTC, because falling back to UTC would silently
/// reintroduce exactly the defect this type exists to fix for every tenant in the only market
/// there is.
/// </remarks>
public sealed class TenantClock : ITenantClock
{
    /// <summary>
    /// The zone the legacy RUMI install has always run on — it was hardcoded in
    /// <c>WorkingHoursService</c>, which is now a reader of this type.
    /// </summary>
    public const string DefaultTimeZoneId = "Europe/Zurich";

    public TenantClock(IOptions<LocalizationSettings> options, ILogger<TenantClock> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        var configured = options.Value.TimeZone;

        TimeZone = Resolve(configured, logger);

        logger.LogInformation(
            "Tenant timezone: {TimeZoneId} (configured: {Configured})",
            TimeZone.Id,
            string.IsNullOrWhiteSpace(configured) ? "<unset>" : configured);
    }

    public TimeZoneInfo TimeZone { get; }

    public DateTimeOffset Now => ToTenantTime(DateTime.UtcNow);

    public DateTimeOffset ToTenantTime(DateTime instant)
    {
        // Unspecified is the shape every DateTime read back out of Npgsql arrives in, and in this
        // database it always means UTC. Naming that here is what keeps `TimeZoneInfo.ConvertTime`
        // from treating it as a LOCAL time on the container's own zone (which is UTC, so the bug
        // would be invisible in production and only appear on a developer's machine).
        var utc = instant.Kind switch
        {
            DateTimeKind.Utc => instant,
            DateTimeKind.Local => instant.ToUniversalTime(),
            _ => DateTime.SpecifyKind(instant, DateTimeKind.Utc)
        };

        return TimeZoneInfo.ConvertTime(new DateTimeOffset(utc), TimeZone);
    }

    private static TimeZoneInfo Resolve(string? configured, ILogger logger)
    {
        var id = string.IsNullOrWhiteSpace(configured) ? DefaultTimeZoneId : configured.Trim();

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            logger.LogWarning(
                ex,
                "Localization:TimeZone '{Configured}' is not a timezone this host knows; using {Fallback}.",
                id,
                DefaultTimeZoneId);

            return id == DefaultTimeZoneId ? TimeZoneInfo.Utc : Resolve(null, logger);
        }
    }
}
