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
/// The IANA id the day was derived on (<c>Localization:TimeZone</c>). Diagnostic, and the honest
/// answer to "whose day is this" — a client that shows a date to a guest can say where it is from.
/// </param>
public record TenantTodayDto(DateOnly Date, string TimeZone);
