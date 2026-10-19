using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence.Migrations;
using RestaurantSystem.IntegrationTests.Features.Payments;

namespace RestaurantSystem.IntegrationTests.Infrastructure;

[Collection("Database Lane 2")]
public sealed class CustomerOrderCheckoutJournalMigrationTests(DatabaseFixture fixture) : IAsyncLifetime
{
    private const string Previous = "20261019115747_" + nameof(AddCashierPrintJobs);
    public Task InitializeAsync() => fixture.ResetDatabaseAsync();
    public Task DisposeAsync() => MigrationTestSafety.RestoreCurrentSchemaAsync(fixture);

    [Fact]
    public async Task Empty_downgrade_and_reupgrade_preserve_prior_operation_and_payment_tables()
    {
        await using var context = fixture.CreateContext();
        var migrator = context.GetService<IMigrator>();
        try
        {
            await migrator.MigrateAsync(Previous);
            await using var connection = new NpgsqlConnection(fixture.ConnectionString);
            await connection.OpenAsync();
            await using var check = new NpgsqlCommand("SELECT to_regclass('public.customer_order_checkout_journals') IS NULL AND to_regclass('public.customer_order_operations') IS NOT NULL AND to_regclass('public.order_checkout_sessions') IS NOT NULL", connection);
            (await check.ExecuteScalarAsync()).Should().Be(true);
            await migrator.MigrateAsync();
            (await context.Set<CustomerOrderCheckoutJournal>().CountAsync()).Should().Be(0);
            (await context.Database.GetAppliedMigrationsAsync()).Last().Should().Be(TestDatabaseCluster.CurrentSchemaMigration);
        }
        finally { await migrator.MigrateAsync(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Unknown_or_known_provider_history_cannot_be_removed_by_downgrade(bool known)
    {
        await using var context = fixture.CreateContext();
        var journal = CustomerOrderCheckoutReplayTests.Journal();
        if (known) journal.ProviderSessionId = "cs_native_fixture";
        context.Set<CustomerOrderCheckoutJournal>().Add(journal);
        await context.SaveChangesAsync();
        var downgrade = () => context.GetService<IMigrator>().MigrateAsync(Previous);
        var error = (await downgrade.Should().ThrowAsync<PostgresException>()).Which;
        error.SqlState.Should().Be(PostgresErrorCodes.CheckViolation);
        error.MessageText.Should().Be("Native checkout recovery history must be retained");
        (await context.Set<CustomerOrderCheckoutJournal>().AsNoTracking().SingleAsync())
            .Should().BeEquivalentTo(journal);
    }

    [Theory]
    [InlineData("zero-order")]
    [InlineData("zero-operation")]
    [InlineData("nonpositive-amount")]
    [InlineData("upper-currency")]
    [InlineData("zero-fee")]
    [InlineData("excess-fee")]
    [InlineData("expired-on-create")]
    [InlineData("unbounded-replay")]
    [InlineData("incomplete-lease")]
    public async Task Database_refuses_inconsistent_frozen_shapes(string scenario)
    {
        await using var context = fixture.CreateContext();
        var journal = CustomerOrderCheckoutReplayTests.Journal();
        switch (scenario)
        {
            case "zero-order": journal.OrderId = Guid.Empty; break;
            case "zero-operation": journal.OperationId = Guid.Empty; break;
            case "nonpositive-amount": journal.AmountMinor = 0; break;
            case "upper-currency": journal.Currency = "CHF"; break;
            case "zero-fee": journal.ApplicationFeeMinor = 0; break;
            case "excess-fee": journal.ApplicationFeeMinor = journal.AmountMinor; break;
            case "expired-on-create": journal.ExpiresAt = journal.StartedAt; break;
            case "unbounded-replay": journal.MaximumCreateRetryAt = journal.StartedAt.AddDays(1); break;
            case "incomplete-lease": journal.LeaseId = Guid.NewGuid(); break;
            default: throw new ArgumentOutOfRangeException(nameof(scenario));
        }
        context.Set<CustomerOrderCheckoutJournal>().Add(journal);
        var error = (await FluentActions.Invoking(() => context.SaveChangesAsync())
            .Should().ThrowAsync<DbUpdateException>()).Which;
        error.InnerException.Should().BeOfType<PostgresException>().Which.SqlState.Should().Be(PostgresErrorCodes.CheckViolation);
    }

    [Fact]
    public async Task One_order_cannot_acquire_a_second_operation_or_provider_request()
    {
        await using (var first = fixture.CreateContext())
        {
            first.Set<CustomerOrderCheckoutJournal>().Add(CustomerOrderCheckoutReplayTests.Journal());
            await first.SaveChangesAsync();
        }
        await using var second = fixture.CreateContext();
        var duplicate = CustomerOrderCheckoutReplayTests.Journal();
        duplicate.OperationId = Guid.NewGuid();
        duplicate.CreateIdempotencyKey = "different-key";
        second.Set<CustomerOrderCheckoutJournal>().Add(duplicate);
        var error = (await FluentActions.Invoking(() => second.SaveChangesAsync())
            .Should().ThrowAsync<DbUpdateException>()).Which;
        error.InnerException.Should().BeOfType<PostgresException>().Which.SqlState.Should().Be(PostgresErrorCodes.UniqueViolation);
    }

    [Fact]
    public async Task A_concurrently_committed_request_is_seen_before_downgrade_can_discard_it()
    {
        await using var context = fixture.CreateContext();
        await context.Database.OpenConnectionAsync();
        var pid = ((NpgsqlConnection)context.Database.GetDbConnection()).ProcessID;
        await using var writer = fixture.CreateContext();
        await using var transaction = await writer.Database.BeginTransactionAsync();
        writer.Set<CustomerOrderCheckoutJournal>().Add(CustomerOrderCheckoutReplayTests.Journal());
        await writer.SaveChangesAsync();
        var migrator = context.GetService<IMigrator>();
        var downgrade = migrator.MigrateAsync(Previous);
        var committed = false;
        try
        {
            (await MigrationTestSafety.WaitForBlockedMigrationAsync(fixture.ConnectionString, pid)).Should().BeTrue();
            await transaction.CommitAsync();
            committed = true;
            var error = (await FluentActions.Invoking(async () => await downgrade)
                .Should().ThrowAsync<PostgresException>()).Which;
            error.SqlState.Should().Be(PostgresErrorCodes.CheckViolation);
            (await context.Set<CustomerOrderCheckoutJournal>().AsNoTracking().SingleAsync()).OrderId
                .Should().Be(CustomerOrderCheckoutReplayTests.Journal().OrderId);
        }
        finally
        {
            if (!committed) await transaction.RollbackAsync();
            try { await downgrade; } catch (PostgresException) { /* Expected preservation guard. */ }
            await migrator.MigrateAsync();
        }
    }
}
