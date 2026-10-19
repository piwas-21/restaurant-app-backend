using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace RestaurantSystem.IntegrationTests.Infrastructure;

internal static class MigrationTestSafety
{
    internal static async Task RestoreCurrentSchemaAsync(DatabaseFixture fixture)
    {
        await using var context = fixture.CreateContext();
        await context.Database.MigrateAsync();
    }

    internal static async Task<bool> WaitForBlockedMigrationAsync(string connectionString, int pid)
    {
        await using var monitor = new NpgsqlConnection(connectionString);
        await monitor.OpenAsync();
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            await using var check = new NpgsqlCommand(
                "SELECT wait_event_type = 'Lock' FROM pg_stat_activity WHERE pid = @pid", monitor);
            check.Parameters.AddWithValue("pid", pid);
            if (await check.ExecuteScalarAsync() is true) return true;
            await Task.Delay(50);
        }
        return false;
    }
}
