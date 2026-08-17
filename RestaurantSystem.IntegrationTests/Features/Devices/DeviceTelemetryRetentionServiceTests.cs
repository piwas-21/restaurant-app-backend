using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RestaurantSystem.Api.BackgroundServices;
using RestaurantSystem.Api.Settings;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Infrastructure;
using Xunit;

namespace RestaurantSystem.IntegrationTests.Features.Devices;

// DeviceTelemetryRetentionService purges DeviceEvents + DeviceOrderReceipts older than the window.
// Data-loss class — owner-approved 30 days (2026-07-20).
[Collection("Database Lane 4")]
public class DeviceTelemetryRetentionServiceTests : IntegrationTestBase
{
    public DeviceTelemetryRetentionServiceTests(DatabaseFixture databaseFixture) : base(databaseFixture) { }

    private DeviceTelemetryRetentionService BuildService(bool enabled = true, int retentionDays = 30)
        => new(
            Factory.Services,
            NullLogger<DeviceTelemetryRetentionService>.Instance,
            Options.Create(new DeviceTelemetryRetentionSettings { Enabled = enabled, RetentionDays = retentionDays }));

    // The audit hook forces CreatedAt=now on insert; override it in a second (Modified) save, which
    // preserves the value, to seed rows that are "old" relative to the retention cutoff.
    private async Task<(Guid oldEvent, Guid recentEvent, Guid oldReceipt, Guid recentReceipt)> SeedTelemetry()
    {
        var deviceId = "dev-" + Guid.NewGuid().ToString("N");
        var oldTime = DateTime.UtcNow.AddDays(-45);

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var oldEvent = new DeviceEvent { DeviceId = deviceId, ClientEventId = Guid.NewGuid().ToString("N"), OccurredAt = oldTime, Level = DeviceEventLevel.Info, Message = "old", CreatedBy = "test" };
        var recentEvent = new DeviceEvent { DeviceId = deviceId, ClientEventId = Guid.NewGuid().ToString("N"), OccurredAt = DateTime.UtcNow, Level = DeviceEventLevel.Info, Message = "recent", CreatedBy = "test" };
        var oldReceipt = new DeviceOrderReceipt { DeviceId = deviceId, OrderId = Guid.NewGuid(), Target = DevicePrintTarget.Cashier, Status = DevicePrintStatus.Printed, ReceivedAt = oldTime, Copies = 1, CreatedBy = "test" };
        var recentReceipt = new DeviceOrderReceipt { DeviceId = deviceId, OrderId = Guid.NewGuid(), Target = DevicePrintTarget.Cashier, Status = DevicePrintStatus.Printed, ReceivedAt = DateTime.UtcNow, Copies = 1, CreatedBy = "test" };

        db.DeviceEvents.AddRange(oldEvent, recentEvent);
        db.DeviceOrderReceipts.AddRange(oldReceipt, recentReceipt);
        await db.SaveChangesAsync();

        oldEvent.CreatedAt = oldTime;
        oldReceipt.CreatedAt = oldTime;
        await db.SaveChangesAsync();

        return (oldEvent.Id, recentEvent.Id, oldReceipt.Id, recentReceipt.Id);
    }

    [Fact]
    public async Task Purge_DeletesRowsOlderThanWindow_KeepsRecent_AndIsIdempotent()
    {
        var (oldEvent, recentEvent, oldReceipt, recentReceipt) = await SeedTelemetry();
        var service = BuildService(enabled: true, retentionDays: 30);

        var firstRun = await service.PurgeExpiredTelemetry(CancellationToken.None);
        var secondRun = await service.PurgeExpiredTelemetry(CancellationToken.None);

        firstRun.Should().Be(2);    // one old event + one old receipt
        secondRun.Should().Be(0);   // idempotent — nothing left to purge

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.DeviceEvents.Any(e => e.Id == oldEvent).Should().BeFalse();
        db.DeviceEvents.Any(e => e.Id == recentEvent).Should().BeTrue();
        db.DeviceOrderReceipts.Any(r => r.Id == oldReceipt).Should().BeFalse();
        db.DeviceOrderReceipts.Any(r => r.Id == recentReceipt).Should().BeTrue();
    }

    [Fact]
    public async Task Purge_WithNonPositiveWindow_IsRefused_ByExecuteGuard()
    {
        // A non-positive RetentionDays must not mass-delete: ExecuteAsync bails before purging. We
        // assert the guard by driving ExecuteAsync briefly and confirming nothing was deleted.
        var (oldEvent, _, oldReceipt, _) = await SeedTelemetry();
        var service = BuildService(enabled: true, retentionDays: 0);

        using var cts = new CancellationTokenSource();
        await service.StartAsync(cts.Token);
        await cts.CancelAsync();
        await service.StopAsync(CancellationToken.None);

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.DeviceEvents.Any(e => e.Id == oldEvent).Should().BeTrue();          // guard refused to purge
        db.DeviceOrderReceipts.Any(r => r.Id == oldReceipt).Should().BeTrue(); // ...both tables
    }
}
