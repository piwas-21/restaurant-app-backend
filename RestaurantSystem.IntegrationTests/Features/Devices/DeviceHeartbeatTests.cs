using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Infrastructure;
using Xunit;

namespace RestaurantSystem.IntegrationTests.Features.Devices;

[Collection("Database Lane 4")]
public class DeviceHeartbeatTests : IntegrationTestBase
{
    public DeviceHeartbeatTests(DatabaseFixture databaseFixture) : base(databaseFixture) { }

    private static object HeartbeatBody() => new
    {
        label = "Kitchen tablet",
        tenantSlug = "rumi",
        platform = "Android",
        appVersion = "1.0.18",
        feedRunning = true,
        lastSuccessfulPollAt = DateTime.UtcNow,
        apiBaseUrl = "https://www.rumirestaurant.ch",
        kitchenPrinter = "192.168.1.50:9100",
        cashierPrinter = "192.168.1.51:9100",
    };

    private async Task<HttpResponseMessage> PostHeartbeatAsync(string? deviceId)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/devices/heartbeat")
        {
            Content = JsonContent.Create(HeartbeatBody()),
        };
        // The device key. #475 made `ApiKeyAuthFilter` fail CLOSED, so this is no longer
        // optional — and it was never harmless to omit: without it this suite reached the
        // endpoint through an unauthenticated door and proved nothing about the guard.
        request.Headers.Add(DeviceApiKeyHeader, TestPrinterApiKey);
        if (deviceId is not null)
            request.Headers.Add("X-Device-Id", deviceId);
        return await Client.SendAsync(request);
    }

    [Fact]
    public async Task Heartbeat_WithDeviceId_PersistsDeviceAndConfig()
    {
        var deviceId = "dev-" + Guid.NewGuid().ToString("N");

        var response = await PostHeartbeatAsync(deviceId);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<bool>>(JsonOptions);
        body!.Success.Should().BeTrue();
        body.Data.Should().BeTrue();

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var device = db.PrinterDevices.SingleOrDefault(d => d.DeviceId == deviceId);
        device.Should().NotBeNull();
        device!.Platform.Should().Be("Android");
        device.FeedRunning.Should().BeTrue();
        device.ApiBaseUrl.Should().Be("https://www.rumirestaurant.ch");
        device.LastHeartbeatAt.Should().BeAfter(DateTime.UtcNow.AddMinutes(-1));
    }

    [Fact]
    public async Task Heartbeat_MissingDeviceIdHeader_ReturnsBadRequest()
    {
        var response = await PostHeartbeatAsync(deviceId: null);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Heartbeat_SameDeviceTwice_UpsertsSingleRow()
    {
        var deviceId = "dev-" + Guid.NewGuid().ToString("N");

        (await PostHeartbeatAsync(deviceId)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await PostHeartbeatAsync(deviceId)).StatusCode.Should().Be(HttpStatusCode.OK);

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.PrinterDevices.Count(d => d.DeviceId == deviceId).Should().Be(1);
    }

    [Fact]
    public async Task Heartbeat_ZonelessPollTimestamp_IsAcceptedNotRejectedByTimestamptz()
    {
        // A zoneless ISO timestamp deserializes to DateTime with Kind=Unspecified; the handler must
        // normalise it to UTC or Npgsql rejects the write to the `timestamptz` column (500).
        var deviceId = "dev-" + Guid.NewGuid().ToString("N");
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/devices/heartbeat")
        {
            Content = JsonContent.Create(new
            {
                platform = "Android",
                feedRunning = true,
                lastSuccessfulPollAt = "2026-07-19T10:00:00",
            }),
        };
        request.Headers.Add(DeviceApiKeyHeader, TestPrinterApiKey);  // #475: the filter fails closed now
        request.Headers.Add("X-Device-Id", deviceId);

        var response = await Client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.PrinterDevices.Single(d => d.DeviceId == deviceId).LastSuccessfulPollAt.Should().NotBeNull();
    }
}
