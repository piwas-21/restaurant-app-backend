namespace RestaurantSystem.Api.Common.Utilities;

/// <summary>
/// Normalises an INSTANT that arrived on the query string before it is compared with a
/// <c>timestamp with time zone</c> column.
/// </summary>
/// <remarks>
/// <para>
/// Model binding <c>?startDate=2026-08-27</c> — or any value without an offset — yields
/// <see cref="DateTimeKind.Unspecified"/>, and Npgsql refuses to write anything but
/// <see cref="DateTimeKind.Utc"/> to a <c>timestamptz</c> parameter:
/// <c>"Cannot write DateTime with Kind=Unspecified to PostgreSQL type 'timestamp with time zone'"</c>.
/// The whole query then fails, so the filter that was meant to narrow a list instead removes it
/// (backend #418).
/// </para>
/// <para>
/// This is for INSTANTS only — a poll cursor, a lower/upper bound on a stored moment. A CALENDAR
/// DAY the operator names ("today's bookings", the till's day) is not an instant: bind it as
/// <see cref="DateOnly"/> and turn it into a window, with <c>ITenantClock.TenantDayWindowUtc</c>
/// when the column stores real instants.
/// </para>
/// </remarks>
public static class QueryInstant
{
    /// <summary>The same moment as a UTC <see cref="DateTime"/>, safe to hand to Npgsql.</summary>
    /// <remarks>
    /// <see cref="DateTimeKind.Unspecified"/> is READ AS UTC — that is this API's documented
    /// contract for its date bounds — while a <see cref="DateTimeKind.Local"/> value (what the
    /// binder produces for an offset-carrying value such as <c>2026-08-27T10:00:00+02:00</c>) is
    /// CONVERTED, because there the caller did state which moment they meant.
    /// </remarks>
    public static DateTime AsUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };

    /// <summary>Nullable overload: <c>null</c> in, <c>null</c> out.</summary>
    public static DateTime? AsUtc(DateTime? value) => value.HasValue ? AsUtc(value.Value) : null;
}
