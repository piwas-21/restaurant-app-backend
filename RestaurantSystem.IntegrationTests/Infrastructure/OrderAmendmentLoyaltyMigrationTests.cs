using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.IntegrationTests.Infrastructure;

[Collection("Database Lane 4")]
public sealed class OrderAmendmentLoyaltyMigrationTests(DatabaseFixture fixture) : IAsyncLifetime
{
    private const string PublishedBillingParent = "20261004172810_AddNativeOrderBillingSnapshots"; // pragma: allowlist secret
    private const string LoyaltyMigration = "20261004211803_AddOrderAmendmentLoyaltyCompensation";
    private static readonly string[] LoyaltyTables =
    [
        "order_billing_award_witnesses",
        "order_billing_unit_award_suppressions",
        "order_billing_award_unit_coverages",
        "order_amendment_loyalty_compensations",
        "order_amendment_loyalty_compensation_units",
        "order_amendment_loyalty_compensation_postings",
        "order_amendment_loyalty_reservations",
        "order_amendment_loyalty_owner_holds"
    ];

    public Task InitializeAsync() => fixture.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Empty_rollback_and_upgrade_restore_loyalty_schema_and_guards()
    {
        try
        {
            await AssertSchemaAsync(present: true);
            await MigrateAsync(PublishedBillingParent);
            await AssertSchemaAsync(present: false);
            await MigrateAsync();
            await AssertSchemaAsync(present: true);
        }
        finally
        {
            await MigrateAsync();
        }
    }

    [Fact]
    public async Task Anonymized_ledger_history_refuses_rollback_and_retains_schema()
    {
        await using (var context = fixture.CreateContext())
        {
            context.FidelityPointsTransactions.Add(new FidelityPointsTransaction
            {
                Id = Guid.NewGuid(),
                UserId = null,
                TransactionType = TransactionType.EarnedClawback,
                Points = -17,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = "loyalty-migration-test"
            });
            await context.SaveChangesAsync();
        }

        var failure = await Assert.ThrowsAsync<PostgresException>(() => MigrateAsync(PublishedBillingParent));
        failure.SqlState.Should().Be(PostgresErrorCodes.CheckViolation);
        failure.MessageText.Should().Be("Native order loyalty history must be retained");
        await AssertSchemaAsync(present: true);
    }

    private async Task MigrateAsync(string? target = null)
    {
        await using var context = fixture.CreateContext();
        await context.Database.MigrateAsync(target);
    }

    private async Task AssertSchemaAsync(bool present)
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        foreach (var table in LoyaltyTables)
        {
            await using var command = new NpgsqlCommand("SELECT to_regclass(@table) IS NOT NULL", connection);
            command.Parameters.AddWithValue("table", table);
            ((bool)(await command.ExecuteScalarAsync())!).Should().Be(present, table);
        }

        await using var guards = new NpgsqlCommand("""
            SELECT COUNT(*) = 4 FROM pg_proc
            WHERE proname IN (
                'validate_order_billing_award_witness_insert',
                'validate_order_billing_unit_award_suppression_insert',
                'verify_order_billing_award_witness_complete',
                'protect_order_billing_awarded_transaction')
            """, connection);
        ((bool)(await guards.ExecuteScalarAsync())!).Should().Be(present, "journal protections follow the migration");

        await using var ownerColumn = new NpgsqlCommand("""
            SELECT is_nullable = 'YES' FROM information_schema.columns
            WHERE table_schema = 'public' AND table_name = 'fidelity_points_transactions'
              AND column_name = 'user_id'
            """, connection);
        ((bool)(await ownerColumn.ExecuteScalarAsync())!).Should().Be(present, "ledger owner can be erased while evidence remains");

        if (present)
        {
            await using var context = fixture.CreateContext();
            (await context.Database.GetAppliedMigrationsAsync()).Last().Should().Be(LoyaltyMigration);
        }
    }
}
