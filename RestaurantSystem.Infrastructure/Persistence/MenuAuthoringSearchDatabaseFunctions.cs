namespace RestaurantSystem.Infrastructure.Persistence;

/// <summary>
/// PostgreSQL functions used by tenant-local menu authoring search. These methods are translated by
/// EF Core and deliberately cannot run in client-side code.
/// </summary>
public static class MenuAuthoringSearchDatabaseFunctions
{
    public static string Normalize(string value) =>
        throw new NotSupportedException("Menu authoring search normalization must run in PostgreSQL.");

    public static string Pattern(string value) =>
        throw new NotSupportedException("Menu authoring search patterns must be built in PostgreSQL.");

    public static string PrefixPattern(string value) =>
        throw new NotSupportedException("Menu authoring prefix patterns must be built in PostgreSQL.");
}
