namespace RestaurantSystem.Api.Common.Services.Interfaces;

/// <summary>
/// The tenant's wall clock. Every instant this system stores is UTC; every instant a HUMAN
/// reads — a mail, an "are we open now" answer — has to be the time on the restaurant's own
/// wall, or it is wrong by an hour or two and says so nowhere (backend #363).
/// </summary>
/// <remarks>
/// One tenant per container, so the zone is process-wide configuration
/// (<c>Localization:TimeZone</c>), not a per-request value. Registered as a singleton beside
/// <c>IEmailLanguageResolver</c>, which is the same shape of decision.
/// </remarks>
public interface ITenantClock
{
    /// <summary>The tenant's timezone, as resolved at startup.</summary>
    TimeZoneInfo TimeZone { get; }

    /// <summary>Now, on the tenant's wall clock, carrying its offset.</summary>
    DateTimeOffset Now { get; }

    /// <summary>
    /// A stored instant on the tenant's wall clock, carrying its offset so a caller can print
    /// the marker that #363 is about.
    /// </summary>
    /// <param name="instant">
    /// A UTC instant. <see cref="DateTimeKind.Unspecified"/> is READ AS UTC, which is what this
    /// database stores (every write is <c>DateTime.UtcNow</c>); a <see cref="DateTimeKind.Local"/>
    /// value is converted from the machine's zone first.
    /// </param>
    DateTimeOffset ToTenantTime(DateTime instant);
}
