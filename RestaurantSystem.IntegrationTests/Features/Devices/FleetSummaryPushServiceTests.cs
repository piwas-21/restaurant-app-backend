using System.Net.Http;
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

// FleetSummaryPushService composes the per-tenant snapshot (device roster + missed-order/error
// counts) sofra's /admin/fleet renders. Only the pure BuildPayloadAsync is exercised here — the
// outbound POST is not (no network in tests).
[Collection("Database Lane 3")]
public class FleetSummaryPushServiceTests : IntegrationTestBase
{
    public FleetSummaryPushServiceTests(DatabaseFixture databaseFixture) : base(databaseFixture) { }

    private FleetSummaryPushService BuildService()
        => new(
            Factory.Services,
            Factory.Services.GetRequiredService<IHttpClientFactory>(),
            NullLogger<FleetSummaryPushService>.Instance,
            Options.Create(new FleetPushSettings
            {
                Enabled = true,
                SofraIngestUrl = "https://sofra.example.com/api/telemetry/fleet",
                Secret = "x",
                TenantSlug = "rumi",
                MissedOrderGraceMinutes = 15,
                MissedOrderLookbackHours = 24,
                RecentErrorWindowHours = 24,
            }));

    private async Task<Guid> SeedConfirmedOrder(DateTime createdAt)
    {
        var id = Guid.NewGuid();
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var order = new Order
        {
            Id = id,
            OrderNumber = $"F-{id:N}".Substring(0, 12),
            Type = OrderType.DineIn,
            Status = OrderStatus.Confirmed,
            PaymentStatus = PaymentStatus.Pending,
            OrderDate = createdAt,
            CreatedBy = "test",
        };
        db.Orders.Add(order);
        await db.SaveChangesAsync();
        order.CreatedAt = createdAt;
        await db.SaveChangesAsync();
        return id;
    }

    [Fact]
    public async Task BuildPayload_ComposesDevices_MissedCount_AndRecentErrors()
    {
        var deviceId = "dev-" + Guid.NewGuid().ToString("N");
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.PrinterDevices.Add(new PrinterDevice
            {
                DeviceId = deviceId,
                Label = "Kitchen tablet",
                Platform = "Android",
                AppVersion = "1.0.20",
                FeedRunning = true,
                LastHeartbeatAt = DateTime.UtcNow,
                CreatedBy = "test",
            });
            db.DeviceEvents.Add(new DeviceEvent
            {
                DeviceId = deviceId,
                ClientEventId = Guid.NewGuid().ToString("N"),
                OccurredAt = DateTime.UtcNow,
                Level = DeviceEventLevel.Error,
                Message = "printer unreachable",
                CreatedBy = "test",
            });
            await db.SaveChangesAsync();
        }
        await SeedConfirmedOrder(DateTime.UtcNow.AddHours(-1)); // confirmed, unprinted, past grace → missed

        var payload = await BuildService().BuildPayloadAsync(CancellationToken.None);

        payload.TenantSlug.Should().Be("rumi");
        payload.Devices.Should().Contain(d => d.DeviceId == deviceId && d.Platform == "Android" && d.FeedRunning);
        payload.RecentErrors.Should().BeGreaterThanOrEqualTo(1);
        payload.MissedOrders.Should().BeGreaterThanOrEqualTo(1);
    }
}
