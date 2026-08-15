using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Infrastructure.Settings;

namespace RestaurantSystem.Api.Common.Services;

/// <summary>
/// <see cref="ITenantClock"/> over <c>Localization:TimeZone</c>.
/// </summary>
/// <remarks>
/// The runtime image (<c>mcr.microsoft.com/dotnet/aspnet</c>, Ubuntu) carries tzdata, verified on
/// the running production container — if that ever stops being true this type degrades to
/// <see cref="TimeZoneInfo.Utc"/> at Warning level, which is #363 again, so the log line at
/// startup is the thing to check after a base-image change.
/// <para>
/// An unknown or unusable zone id logs and falls back to the default rather than throwing: a typo
/// in one tenant's <c>.env</c> must not stop that tenant booting — the same call
/// <c>EmailLanguageResolver</c> makes about an unusable language list. The fallback is the
/// PRODUCT default (<c>Europe/Zurich</c>), not UTC, because falling back to UTC would silently
/// reintroduce exactly the defect this type exists to fix for every tenant in the only market
/// there is.
/// </para>
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
        // Npgsql returns Kind=Utc for the `timestamp with time zone` columns this schema uses, so
        // the branch that matters in production is the first one. The other two are defensive, and
        // Unspecified means UTC here because every write in this system is `DateTime.UtcNow`.
        // Naming it keeps `TimeZoneInfo.ConvertTime` from reading such a value as a LOCAL time on
        // the container's own zone — which IS UTC, so that bug would be invisible in production
        // and appear only on a developer's machine.
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
