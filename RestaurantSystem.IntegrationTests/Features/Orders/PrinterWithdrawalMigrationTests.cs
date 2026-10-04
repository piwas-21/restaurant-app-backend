using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence.Migrations;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

[Collection("Database Lane 4")]
public sealed class PrinterWithdrawalMigrationTests(DatabaseFixture fixture) : IntegrationTestBase(fixture)
{
    private const string PublishedParent = "20261004172810_" + nameof(AddNativeOrderBillingSnapshots);

    [Fact]
    public async Task Empty_withdrawal_migration_can_downgrade_and_reapply_without_model_drift()
    {
        await using var context = DatabaseFixture.CreateContext();
        var migrator = context.GetService<IMigrator>();
        try
        {
            await migrator.MigrateAsync(PublishedParent);
            await migrator.MigrateAsync();
            Assert.False(context.Database.HasPendingModelChanges());
            var names = await context.Database.SqlQuery<string>($"""
                SELECT column_name AS "Value" FROM information_schema.columns
                WHERE table_name = 'OrderOperationalNotes'
                AND column_name IN ('withdrawn_at', 'FeedEventAt')
                """).ToArrayAsync();
            Assert.Equal(2, names.Length);
        }
        finally
        {
            await migrator.MigrateAsync();
        }
    }

    [Fact]
    public async Task Withdrawal_evidence_refuses_downgrade_and_remains_available_for_cached_job_revocation()
    {
        await using var context = DatabaseFixture.CreateContext();
        var now = DateTime.UtcNow;
        var note = new OrderOperationalNote
        {
            Id = Guid.NewGuid(),
            ClientOperationId = Guid.NewGuid(),
            Audience = OrderNoteAudience.Kitchen,
            Text = "[erased]",
            WithdrawnAt = now,
            CreatedAt = now.AddMinutes(-1),
            CreatedBy = "withdrawal-migration-test",
            Order = new Order
            {
                Id = Guid.NewGuid(),
                OrderNumber = $"MIG-{Guid.NewGuid():N}"[..20],
                Type = OrderType.Takeaway,
                Status = OrderStatus.Completed,
                PaymentStatus = PaymentStatus.Completed,
                Total = 12.35m,
                OrderDate = now,
                CreatedAt = now,
                CreatedBy = "withdrawal-migration-test"
            }
        };
        context.OrderOperationalNotes.Add(note);
        await context.SaveChangesAsync();
        var failure = await Assert.ThrowsAsync<PostgresException>(() =>
            context.GetService<IMigrator>().MigrateAsync(PublishedParent));
        Assert.Contains("withdrawal evidence prevents", failure.MessageText);
        await using var verify = DatabaseFixture.CreateContext();
        var retained = await verify.OrderOperationalNotes.AsNoTracking().SingleAsync(value => value.Id == note.Id);
        Assert.NotNull(retained.WithdrawnAt);
        Assert.Equal("[erased]", retained.Text);
        Assert.Contains("20261004220409_" + nameof(WithdrawRetainedPrinterInstructions),
            await verify.Database.GetAppliedMigrationsAsync());
    }
}
