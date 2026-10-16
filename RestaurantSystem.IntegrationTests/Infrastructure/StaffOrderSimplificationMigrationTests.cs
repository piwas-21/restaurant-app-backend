using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.IntegrationTests.Infrastructure;

[Collection("Database Lane 2")]
public sealed class StaffOrderSimplificationMigrationTests(DatabaseFixture fixture) : IAsyncLifetime
{
    public Task InitializeAsync() => fixture.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Qr_only_participant_refuses_downgrade_without_losing_guest_or_ancestry_schema()
    {
        await using var context = fixture.CreateContext();
        var session = new TableServiceSession { Id = Guid.NewGuid(), OpenedAt = DateTime.UtcNow, CreatedBy = nameof(StaffOrderSimplificationMigrationTests) };
        var participant = new TableGuestParticipant
        {
            Id = Guid.NewGuid(),
            ServiceSessionId = session.Id,
            AdmissionId = null,
            CreatedBy = nameof(StaffOrderSimplificationMigrationTests),
            TokenHash = new string('A', 64),
            ExpiresAt = DateTime.UtcNow.AddHours(1),
        };
        context.TableServiceSessions.Add(session);
        context.TableGuestParticipants.Add(participant);
        await context.SaveChangesAsync();
        var migrator = context.GetService<IMigrator>();
        var downgrade = () => migrator.MigrateAsync("20261015123015_AddDailyOrderOperations");
        (await downgrade.Should().ThrowAsync<PostgresException>()).Which.MessageText
            .Should().Contain("QR-only participants");
        (await context.TableGuestParticipants.AsNoTracking().SingleAsync()).Id.Should().Be(participant.Id);
        // The guard executes before DROP COLUMN, so ordinary queries still bind the ancestry field.
        (await context.Orders.Select(order => order.AmendmentRootOrderId).ToListAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task Empty_downgrade_and_upgrade_remain_available_for_historical_migration_tests()
    {
        await using var context = fixture.CreateContext();
        var migrator = context.GetService<IMigrator>();
        await migrator.MigrateAsync("20261015123015_AddDailyOrderOperations");
        await migrator.MigrateAsync();
        (await context.Database.GetAppliedMigrationsAsync()).Should().Contain("20261016175405_StaffOrderSimplification");
        (await context.TableGuestParticipants.CountAsync()).Should().Be(0);
    }
}
