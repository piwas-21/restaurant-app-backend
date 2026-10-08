using Microsoft.EntityFrameworkCore;
using Npgsql;
using RestaurantSystem.Api.Features.OrderAmendments.Dtos;
using RestaurantSystem.Api.Features.OrderAmendments.Services;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Common;

namespace RestaurantSystem.IntegrationTests.Infrastructure;

[Collection("Database Lane 4")]
public sealed class NativeOrderBillingAwardJournalMigrationTests(DatabaseFixture fixture) : IAsyncLifetime
{
    private const string SnapshotMigration = "20261004172810_AddNativeOrderBillingSnapshots"; // pragma: allowlist secret
    private const string AwardJournalMigration = "20261004211803_AddOrderAmendmentLoyaltyCompensation"; // pragma: allowlist secret

    public Task InitializeAsync() => fixture.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Full_native_loyalty_migration_rolls_back_to_published_billing_and_reapplies()
    {
        try
        {
            await MigrateAsync(SnapshotMigration);
            await using (var rolledBack = fixture.CreateContext())
            {
                var applied = await rolledBack.Database.GetAppliedMigrationsAsync();
                Assert.DoesNotContain(AwardJournalMigration, applied);
                Assert.False(await TableExistsAsync("order_billing_award_witnesses"));
                Assert.False(await TableExistsAsync("order_billing_unit_award_suppressions"));
            }

            await MigrateAsync();
            await using var restored = fixture.CreateContext();
            var restoredMigrations = await restored.Database.GetAppliedMigrationsAsync();
            Assert.Contains(AwardJournalMigration, restoredMigrations);
            Assert.True(await TableExistsAsync("order_billing_award_witnesses"));
            Assert.True(await TableExistsAsync("order_billing_unit_award_suppressions"));
            Assert.True(await TableExistsAsync("order_amendment_loyalty_compensations"));
            Assert.True(await TableExistsAsync("order_amendment_loyalty_owner_holds"));
        }
        finally
        {
            await MigrateAsync();
        }
    }

    [Fact]
    public async Task Native_loyalty_migration_refuses_down_when_suppression_history_exists()
    {
        var suppressionId = await SeedValidSuppressionAsync();
        try
        {
            var exception = await Assert.ThrowsAsync<PostgresException>(() => MigrateAsync(SnapshotMigration));

            Assert.Equal(PostgresErrorCodes.CheckViolation, exception.SqlState);
            Assert.Contains("Native order loyalty history must be retained", exception.Message);
            await using var verify = fixture.CreateContext();
            Assert.Contains(AwardJournalMigration, await verify.Database.GetAppliedMigrationsAsync());
            Assert.Equal(1, await verify.OrderBillingUnitAwardSuppressions.CountAsync(value => value.Id == suppressionId));
            Assert.True(await TableExistsAsync("order_billing_award_witnesses"));
            Assert.True(await TableExistsAsync("order_billing_unit_award_suppressions"));
        }
        finally
        {
            await MigrateAsync();
        }
    }

    [Fact]
    public async Task Fresh_postgres_database_migrates_from_zero_through_published_billing_and_new_loyalty_schema()
    {
        var baseBuilder = new NpgsqlConnectionStringBuilder(fixture.ConnectionString)
        {
            Database = "postgres",
            Pooling = false
        };
        var database = $"native_loyalty_{Guid.NewGuid():N}";
        await using (var admin = new NpgsqlConnection(baseBuilder.ConnectionString))
        {
            await admin.OpenAsync();
            await using var create = new NpgsqlCommand($"CREATE DATABASE \"{database}\"", admin);
            await create.ExecuteNonQueryAsync();
        }

        var candidateBuilder = new NpgsqlConnectionStringBuilder(fixture.ConnectionString)
        {
            Database = database,
            Pooling = false
        };
        var dataSourceBuilder = new NpgsqlDataSourceBuilder(candidateBuilder.ConnectionString);
        dataSourceBuilder.EnableDynamicJson();
        await using var dataSource = dataSourceBuilder.Build();
        try
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(dataSource).Options;
            await using (var fresh = new ApplicationDbContext(options))
            {
                await fresh.Database.MigrateAsync();
                var applied = await fresh.Database.GetAppliedMigrationsAsync();
                Assert.Contains(SnapshotMigration, applied);
                Assert.Contains(AwardJournalMigration, applied);
                Assert.True(await RelationExistsAsync(fresh, "order_amendment_loyalty_compensations"));
                Assert.True(await RelationExistsAsync(fresh, "order_amendment_loyalty_owner_holds"));
                Assert.True(await UserIdIsNullableAsync(fresh));

                await fresh.Database.MigrateAsync(SnapshotMigration);
                Assert.False(await RelationExistsAsync(fresh, "order_amendment_loyalty_compensations"));
                Assert.False(await UserIdIsNullableAsync(fresh));

                await fresh.Database.MigrateAsync();
                Assert.Contains(AwardJournalMigration, await fresh.Database.GetAppliedMigrationsAsync());
                Assert.True(await RelationExistsAsync(fresh, "order_amendment_loyalty_compensations"));
                Assert.True(await UserIdIsNullableAsync(fresh));
            }
        }
        finally
        {
            NpgsqlConnection.ClearAllPools();
            await using var admin = new NpgsqlConnection(baseBuilder.ConnectionString);
            await admin.OpenAsync();
            await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{database}\" WITH (FORCE)", admin);
            await drop.ExecuteNonQueryAsync();
        }
    }

    private async Task<Guid> SeedValidSuppressionAsync()
    {
        await using var context = fixture.CreateContext();
        var userId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        await TestUserSeeder.SeedUserAsync(context, userId);
        await TestOrderSeeder.SeedOrderAsync(context, orderId, userId);
        var order = await context.Orders.SingleAsync(value => value.Id == orderId);
        order.Status = OrderStatus.Completed;
        order.PaymentStatus = PaymentStatus.Completed;
        order.SubTotal = 40m;
        order.Total = 40m;
        order.FidelityPointsEarned = 80;
        var item = new OrderItem
        {
            Id = itemId,
            OrderId = orderId,
            ProductName = "Award journal migration control",
            Quantity = 1,
            UnitPrice = 40m,
            ItemTotal = 40m,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "AwardJournalMigrationTest"
        };
        order.Items.Add(item);
        context.OrderItems.Add(item);
        await context.SaveChangesAsync();

        var rule = new OrderBillingEarningRuleEvidence(Guid.NewGuid(), "Migration control rule", 0m,
            null, 80, 1);
        var evaluation = new OrderBillingEarningEvaluation(80, "fixed-test-v1", new string('a', 64), rule);
        var built = OrderBillingSnapshotFactory.Build(order, "CHF", evaluation, null,
            OrderBillingSnapshotLimits.AbsoluteMaximumUnitRows);
        await using (var evidenceTransaction = await context.Database.BeginTransactionAsync())
        {
            await LegacyOrderBillingSnapshotFixture.InsertAsync(context, built.Header);
            context.OrderBillingSnapshotUnits.AddRange(built.Units);
            context.OrderBillingSnapshotOwnerLinks.AddRange(built.OwnerLinks);
            await context.SaveChangesAsync();
            await evidenceTransaction.CommitAsync();
        }

        var unit = built.Units.Single();
        var change = new OrderAmendmentChangeSnapshot(unit.OrderItemId, OrderAmendmentChangeKind.Void,
            unit.UnitOrdinal, 1, false, new OrderItemDto
            {
                Id = itemId,
                ProductName = item.ProductName,
                Quantity = item.Quantity,
                UnitPrice = item.UnitPrice,
                ItemTotal = item.ItemTotal
            }, null);
        var now = DateTime.UtcNow;
        var amendment = new OrderAmendment
        {
            Id = Guid.NewGuid(),
            SourceOrderId = orderId,
            ActorUserId = userId,
            ActorRole = "Admin",
            State = OrderAmendmentState.Committed,
            PayloadHash = new string('b', 64),
            CommitPayloadHash = new string('c', 64),
            ExpectedOrderVersion = 1,
            ExpiresAt = now.AddMinutes(5),
            CommittedAt = now,
            RequestJson = "{}",
            ChangesJson = OrderAmendmentJson.Serialize(new[] { change }),
            SourceSnapshotJson = "{}",
            FinancialResolutionJson = "{}",
            CreatedAt = now,
            CreatedBy = "AwardJournalMigrationTest"
        };
        context.OrderAmendments.Add(amendment);
        await context.SaveChangesAsync();

        await using var transaction = await context.Database.BeginTransactionAsync();
        await new OrderBillingAwardSuppressionWriter(context).RecordRemovedUnitsAsync(
            orderId, amendment.Id, CancellationToken.None);
        await transaction.CommitAsync();
        var row = await context.OrderBillingUnitAwardSuppressions.AsNoTracking()
            .SingleAsync(value => value.OrderId == orderId);
        return row.Id;
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

    private static async Task<bool> RelationExistsAsync(ApplicationDbContext context, string relation) =>
        await context.Database.SqlQuery<bool>($"SELECT to_regclass({relation}) IS NOT NULL AS \"Value\"")
            .SingleAsync();

    private static async Task<bool> UserIdIsNullableAsync(ApplicationDbContext context) =>
        await context.Database.SqlQuery<bool>($"""
            SELECT is_nullable = 'YES' AS "Value" FROM information_schema.columns
            WHERE table_schema = 'public' AND table_name = 'fidelity_points_transactions'
                AND column_name = 'user_id'
            """).SingleAsync();
}
