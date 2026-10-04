namespace RestaurantSystem.Api.Features.AccountPayments.Services;

/// <summary>Compares JSON timestamps with PostgreSQL's microsecond-resolution timestamp columns.</summary>
internal static class PostgresTimestampPrecision
{
    private const long TicksPerMicrosecond = 10;

    internal static bool MatchesColumn(DateTime? serialized, DateTime? persisted)
    {
        if (serialized.HasValue != persisted.HasValue)
            return false;
        if (serialized is null)
            return true;

        var column = persisted!.Value;
        return column.Ticks % TicksPerMicrosecond == 0
            && TruncateToMicrosecond(serialized.Value).Ticks == column.Ticks;
    }

    internal static DateTime TruncateToMicrosecond(DateTime value) =>
        new(value.Ticks - value.Ticks % TicksPerMicrosecond, value.Kind);
}
