using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Moq;
using Npgsql;
using RestaurantSystem.Api.Features.AccountPayments.Services;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence.Migrations;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Payments;

public sealed partial class AccountCheckoutJournalTests
{
    private const string BeforeNativeScope = "20261023010734_" + nameof(AddCustomerTablePaymentOperations);

    [Fact]
    public async Task Native_scope_empty_rollback_preserves_ordinary_provider_payload_and_money()
    {
        var seed = await Seed();
        await using var context = DatabaseFixture.CreateContext();
        var journal = await Store(context, seed.Actor, Provider().Object, Mock.Of<IGuestAccountPaymentPolicy>())
            .FreezeGuestAsync(seed.SessionId, seed.OperationId, 1, "participant-credential", Credential, CancellationToken.None);
        var original = AccountCheckoutReplayPayload.Frozen(journal);
        var hash = journal.CreatePayloadHash;
        try
        {
            await context.GetService<IMigrator>().MigrateAsync(BeforeNativeScope);
            await using var connection = new NpgsqlConnection(DatabaseFixture.ConnectionString); await connection.OpenAsync();
            await using var check = new NpgsqlCommand("SELECT count(*) FROM account_checkout_journals WHERE create_payload_hash = @hash AND amount_minor = 334", connection);
            check.Parameters.AddWithValue("hash", hash);
            (await check.ExecuteScalarAsync()).Should().Be(1L);
        }
        finally { await context.GetService<IMigrator>().MigrateAsync(); }
        context.ChangeTracker.Clear();
        var saved = await context.AccountCheckoutJournals.SingleAsync();
        saved.NativeOperationId.Should().BeNull(); saved.NativeTenantId.Should().BeNull();
        AccountCheckoutReplayPayload.Frozen(saved).Should().BeEquivalentTo(original);
        saved.CreatePayloadHash.Should().Be(hash);
    }

    [Fact]
    public async Task Retained_native_provider_request_refuses_scope_removal()
    {
        var seed = await Seed();
        await using var context = DatabaseFixture.CreateContext();
        var journal = await Store(context, seed.Actor, Provider().Object, Mock.Of<IGuestAccountPaymentPolicy>())
            .FreezeGuestAsync(seed.SessionId, seed.OperationId, 1, "participant-credential", Credential, CancellationToken.None);
        var operation = new CustomerTablePaymentOperation
        {
            OperationId = Guid.NewGuid(),
            OwnerHash = new string('a', 64),
            PayloadHash = new string('b', 64),
            AttemptId = journal.AttemptId,
            CompletedAt = Now
        };
        context.Set<CustomerTablePaymentOperation>().Add(operation);
        journal.NativeOperationId = operation.OperationId; journal.NativeTenantId = Guid.NewGuid();
        await context.SaveChangesAsync();
        try
        {
            var downgrade = () => context.GetService<IMigrator>().MigrateAsync(BeforeNativeScope);
            (await downgrade.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be(PostgresErrorCodes.CheckViolation);
            // Restore any newer empty additive tables already removed before the guarded refusal.
            await context.GetService<IMigrator>().MigrateAsync();
            (await context.Database.GetAppliedMigrationsAsync()).Last().Should().Be(TestDatabaseCluster.CurrentSchemaMigration);
            context.ChangeTracker.Clear();
            var retained = await context.AccountCheckoutJournals.SingleAsync();
            retained.NativeOperationId.Should().Be(operation.OperationId); retained.AmountMinor.Should().Be(334);
            retained.CreatePayloadHash.Should().Be(journal.CreatePayloadHash);
        }
        finally { await context.GetService<IMigrator>().MigrateAsync(); }
    }

    [Fact]
    public async Task In_flight_provider_request_refuses_scope_removal_without_waiting()
    {
        var seed = await Seed();
        await using var context = DatabaseFixture.CreateContext();
        var journal = await Store(context, seed.Actor, Provider().Object, Mock.Of<IGuestAccountPaymentPolicy>())
            .FreezeGuestAsync(seed.SessionId, seed.OperationId, 1, "participant-credential", Credential, CancellationToken.None);
        await using var connection = new NpgsqlConnection(DatabaseFixture.ConnectionString); await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await using var update = new NpgsqlCommand("UPDATE account_checkout_journals SET last_failure_code = 'migration-race' WHERE id = @id", connection, transaction);
        update.Parameters.AddWithValue("id", journal.Id); await update.ExecuteNonQueryAsync();
        try
        {
            var downgrade = () => context.GetService<IMigrator>().MigrateAsync(BeforeNativeScope);
            var refusal = (await downgrade.Should().ThrowAsync<InvalidOperationException>()).Which;
            refusal.InnerException.Should().BeOfType<PostgresException>().Which.SqlState.Should().Be(PostgresErrorCodes.LockNotAvailable);
            await transaction.CommitAsync(); context.ChangeTracker.Clear();
            (await context.AccountCheckoutJournals.SingleAsync()).LastFailureCode.Should().Be("migration-race");
            // Restore any newer empty additive tables already removed before the guarded refusal.
            await context.GetService<IMigrator>().MigrateAsync();
            (await context.Database.GetAppliedMigrationsAsync()).Last().Should().Be(TestDatabaseCluster.CurrentSchemaMigration);
        }
        finally { await context.GetService<IMigrator>().MigrateAsync(); }
    }
}
