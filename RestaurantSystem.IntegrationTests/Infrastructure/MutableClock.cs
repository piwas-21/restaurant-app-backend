using RestaurantSystem.Api.Common.Services.Interfaces;

namespace RestaurantSystem.IntegrationTests.Infrastructure;

/// <summary>
/// A clock stopped at an instant the test picks, in a zone the test picks. Hand-written rather than
/// mocked: the point of <see cref="ITenantClock"/> being an interface is that a test can hold one.
/// <para>
/// Lifted out of <c>ReservationClockTests</c>, where it was a private nested class, when a third
/// test class needed it. Two copies is a coincidence; three is a helper.
/// </para>
/// </summary>
public sealed class MutableClock : ITenantClock
{
    private DateTimeOffset _instant;

    public MutableClock(DateTimeOffset instant, string zoneId)
    {
        _instant = instant;
        TimeZone = TimeZoneInfo.FindSystemTimeZoneById(zoneId);
    }

    public TimeZoneInfo TimeZone { get; private set; }

    public DateTimeOffset Now => ToTenantTime(_instant.UtcDateTime);

    public void Set(DateTimeOffset instant, string zoneId)
    {
        _instant = instant;
        TimeZone = TimeZoneInfo.FindSystemTimeZoneById(zoneId);
    }

    public DateTimeOffset ToTenantTime(DateTime instant) =>
        TimeZoneInfo.ConvertTime(new DateTimeOffset(DateTime.SpecifyKind(instant, DateTimeKind.Utc)), TimeZone);
}
