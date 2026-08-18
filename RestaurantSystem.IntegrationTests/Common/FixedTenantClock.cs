using RestaurantSystem.Api.Common.Services.Interfaces;

namespace RestaurantSystem.IntegrationTests.Common;

/// <summary>
/// An <see cref="ITenantClock"/> a test owns: a zone it picks, and either an instant it pins or the
/// real one. Shared by the tests of backend #372 because three of them need the same double; the
/// older clock tests each carry their own and are deliberately left alone.
/// </summary>
/// <remarks>
/// The conversion is the same one <c>TenantClock</c> performs, but the day-window derivation is NOT
/// duplicated here — it is an extension method over this interface precisely so a double inherits
/// it instead of re-deriving the boundary rule its own way and agreeing with itself.
/// </remarks>
internal sealed class FixedTenantClock : ITenantClock
{
    private readonly DateTimeOffset? _instant;

    /// <param name="zoneId">IANA id; must exist in both the CI SDK image and the aspnet runtime image.</param>
    /// <param name="instant">The instant to stop at, or <c>null</c> to follow the real clock.</param>
    public FixedTenantClock(string zoneId, DateTimeOffset? instant = null)
    {
        TimeZone = TimeZoneInfo.FindSystemTimeZoneById(zoneId);
        _instant = instant;
    }

    public TimeZoneInfo TimeZone { get; }

    public DateTimeOffset Now => ToTenantTime((_instant ?? DateTimeOffset.UtcNow).UtcDateTime);

    public DateTimeOffset ToTenantTime(DateTime instant) =>
        TimeZoneInfo.ConvertTime(
            new DateTimeOffset(DateTime.SpecifyKind(instant, DateTimeKind.Utc)), TimeZone);
}
