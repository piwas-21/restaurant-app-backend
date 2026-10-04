using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace RestaurantSystem.IntegrationTests.Infrastructure;

[Collection("Database Lane 4")]
public sealed class PaidAmendmentResolutionMigrationTests(DatabaseFixture fixture) : IAsyncLifetime
{
    private const string BeforePaidResolution = "20261003120021_AddTableVisitReadiness";
    private const string LatestMigration = "20261004172810_AddNativeOrderBillingSnapshots"; // pragma: allowlist secret
    private static readonly string[] PaidTables =
    [
        "order_amendment_resolution_operations",
        "order_amendment_refund_legs",
        "order_amendment_refund_attempts",
        "order_amendment_refund_evidence",
        "account_payment_allocation_reversals",
        "order_amendment_resolution_refusals",
        "account_cash_collection_receipts",
        "account_cash_refund_intents",
        "account_cash_refund_evidence"
    ];

    public Task InitializeAsync() => fixture.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Empty_paid_resolution_rollback_and_upgrade_round_trip_all_tables()
    {
        try
        {
            await MigrateAsync(BeforePaidResolution);
            foreach (var table in PaidTables)
                (await TableExistsAsync(table)).Should().BeFalse($"{table} should be removed by an empty rollback");

            await MigrateAsync();
            (await LatestAppliedMigrationAsync()).Should().Be(LatestMigration);
            foreach (var table in PaidTables)
                (await TableExistsAsync(table)).Should().BeTrue($"{table} should be recreated on upgrade");
        }
        finally
        {
            await MigrateAsync();
        }
    }

    private async Task MigrateAsync(string? target = null)
    {
        await using var context = fixture.CreateContext();
        await context.Database.MigrateAsync(target);
    }

    private async Task<bool> TableExistsAsync(string table)
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT to_regclass(@table) IS NOT NULL", connection);
        command.Parameters.AddWithValue("table", table);
        return (bool)(await command.ExecuteScalarAsync())!;
    }

    private async Task<string> LatestAppliedMigrationAsync()
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT \"MigrationId\" FROM \"__EFMigrationsHistory\" ORDER BY \"MigrationId\" DESC LIMIT 1",
            connection);
        return (string)(await command.ExecuteScalarAsync())!;
    }
}
