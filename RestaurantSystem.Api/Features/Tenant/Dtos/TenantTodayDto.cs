namespace RestaurantSystem.Api.Features.Tenant.Dtos;

/// <summary>
/// What day it is at the restaurant, and on whose clock (frontend #517).
/// </summary>
/// <param name="Date">
/// The tenant's current CALENDAR day — no time, no offset. Serialized as
/// <c>"2026-08-19"</c>, which is the shape every date-taking endpoint here binds
/// (<see cref="DateOnly"/>), so a client can hand it straight back without inventing an instant.
/// </param>
/// <param name="TimeZone">
/// The zone the day was derived on. Diagnostic, and the honest answer to "whose day is this" — a
/// client that shows a date to a guest can say where it is from.
/// <para>
/// It is the EFFECTIVE zone, not the configured one: <c>TenantClock</c> falls back to
/// <c>Europe/Zurich</c> when <c>Localization:TimeZone</c> names a zone the host does not know, and
/// this reports what the day was actually computed on. A typo therefore shows up here as the wrong
/// zone rather than as an error, which is deliberate — a tenant must boot — but it means this
/// field is the place to look when a tenant's day looks off by one.
/// </para>
/// <para>
/// It is <c>TimeZoneInfo.Id</c>, which is an IANA id on every host this ships to (the linux
/// <c>aspnet</c> image, the ubuntu CI runner). On Windows the same lookup answers
/// <c>"W. Europe Standard Time"</c>, which a browser's <c>Intl.DateTimeFormat</c> would reject —
/// nothing computes with this field today, and a client that starts to must convert first.
/// </para>
/// </param>
public record TenantTodayDto(DateOnly Date, string TimeZone);
