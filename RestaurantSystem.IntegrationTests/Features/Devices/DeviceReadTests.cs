using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Devices.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Infrastructure;
using Xunit;

namespace RestaurantSystem.IntegrationTests.Features.Devices;

[Collection("Database Lane 1")]
public class DeviceReadTests : IntegrationTestBase
{
    public DeviceReadTests(DatabaseFixture databaseFixture) : base(databaseFixture) { }

    private static string NewDeviceId() => "dev-" + Guid.NewGuid().ToString("N");

    private ApplicationDbContext Db(IServiceScope scope) =>
        scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

    private async Task PostAsync(string path, object body, string deviceId)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
        request.Headers.Add("X-Device-Id", deviceId);
        (await Client.SendAsync(request)).EnsureSuccessStatusCode();
    }

    private async Task SeedHeartbeat(string deviceId) =>
        await PostAsync("/api/devices/heartbeat", new { platform = "Android", feedRunning = true }, deviceId);

    // Insert a confirmed order at a controlled CreatedAt. The audit hook forces CreatedAt=now on
    // insert, so we override it in a second (Modified) save, which preserves the value.
    private async Task<Guid> SeedConfirmedOrder(DateTime createdAt)
    {
        var id = Guid.NewGuid();
        using var scope = Factory.Services.CreateScope();
        var db = Db(scope);
        var order = new Order
        {
            Id = id,
            OrderNumber = $"M-{id:N}".Substring(0, 12),
            Type = OrderType.DineIn,
            TableNumber = 7,
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

    private async Task SeedPrintedReceipt(Guid orderId, string deviceId)
    {
        using var scope = Factory.Services.CreateScope();
        var db = Db(scope);
        db.DeviceOrderReceipts.Add(new DeviceOrderReceipt
        {
            DeviceId = deviceId,
            OrderId = orderId,
            Target = DevicePrintTarget.FrontKitchen,
            Status = DevicePrintStatus.Printed,
            ReceivedAt = DateTime.UtcNow,
            PrintedAt = DateTime.UtcNow,
            Copies = 1,
            CreatedBy = "test",
        });
        await db.SaveChangesAsync();
    }

    // ---- device list / events (admin) --------------------------------------

    [Fact]
    public async Task GetDevices_AsAdmin_ReturnsSeededDevice()
    {
        var deviceId = NewDeviceId();
        await SeedHeartbeat(deviceId);

        AuthenticateAsAdmin();
        var response = await Client.GetAsync("/api/devices");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<List<DeviceSummaryDto>>>(JsonOptions);
        body!.Data.Should().Contain(d => d.DeviceId == deviceId && d.Platform == "Android" && d.FeedRunning);
    }

    [Fact]
    public async Task GetDevices_WithoutAdmin_IsRejected()
    {
        var response = await Client.GetAsync("/api/devices");

        response.StatusCode.Should().BeOneOf(HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetDeviceEvents_AsAdmin_ReturnsDeviceEvents()
    {
        var deviceId = NewDeviceId();
        var clientEventId = Guid.NewGuid().ToString("N");
        await PostAsync("/api/devices/events", new
        {
            events = new[]
            {
                new { clientEventId, occurredAt = DateTime.UtcNow, level = "Error", code = "X", message = "boom", context = (string?)null },
            },
        }, deviceId);

        AuthenticateAsAdmin();
        var response = await Client.GetAsync($"/api/devices/{deviceId}/events");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<List<DeviceEventLogDto>>>(JsonOptions);
        body!.Data.Should().ContainSingle(e => e.ClientEventId == clientEventId && e.Level == DeviceEventLevel.Error);
    }

    // ---- missed-order detection --------------------------------------------

    [Fact]
    public async Task GetMissedOrders_ConfirmedOldUnprinted_IsReported()
    {
        var orderId = await SeedConfirmedOrder(DateTime.UtcNow.AddHours(-1));

        AuthenticateAsAdmin();
        var response = await Client.GetAsync("/api/devices/missed-orders?graceMinutes=15");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<List<MissedOrderDto>>>(JsonOptions);
        body!.Data.Should().Contain(o => o.OrderId == orderId && o.TableNumber == 7);
    }

    [Fact]
    public async Task GetMissedOrders_PrintedOrWithinGrace_AreExcluded()
    {
        var printedOld = await SeedConfirmedOrder(DateTime.UtcNow.AddHours(-1));
        await SeedPrintedReceipt(printedOld, NewDeviceId());
        var recent = await SeedConfirmedOrder(DateTime.UtcNow.AddMinutes(-2));

        AuthenticateAsAdmin();
        var response = await Client.GetAsync("/api/devices/missed-orders?graceMinutes=15");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<List<MissedOrderDto>>>(JsonOptions);
        body!.Data.Should().NotContain(o => o.OrderId == printedOld);   // printed → accounted for
        body.Data.Should().NotContain(o => o.OrderId == recent);        // within grace → not yet missed
    }

    [Fact]
    public async Task GetMissedOrders_OlderThanLookback_IsExcluded()
    {
        // Without a lookback floor, the whole back-catalogue of old Confirmed-but-never-advanced
        // orders (which have no receipt) would show as false "missed". A 48h-old order must not.
        var ancient = await SeedConfirmedOrder(DateTime.UtcNow.AddHours(-48));

        AuthenticateAsAdmin();
        var response = await Client.GetAsync("/api/devices/missed-orders?graceMinutes=15&lookbackHours=24");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<List<MissedOrderDto>>>(JsonOptions);
        body!.Data.Should().NotContain(o => o.OrderId == ancient);
    }
}
