using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Devices.Dtos;
using RestaurantSystem.Api.Features.Devices.Queries.GetDevicesQuery;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Infrastructure;
using Xunit;

namespace RestaurantSystem.IntegrationTests.Features.Devices;

[Collection("Database Lane 4")]
public class PrinterUpdateAuthorizationCapabilityTests(DatabaseFixture databaseFixture)
    : IntegrationTestBase(databaseFixture)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Heartbeat_ReportsSupport_AndLegacyHeartbeatClearsIt(bool supported)
    {
        var deviceId = "withdrawal-" + Guid.NewGuid().ToString("N");
        await PostAsync(deviceId, new { supportsUpdateAuthorization = supported });
        (await ReadSummaryAsync(deviceId)).SupportsUpdateAuthorization.Should().Be(supported);

        await PostAsync(deviceId, new { feedRunning = true });
        var legacy = await ReadSummaryAsync(deviceId);
        legacy.SupportsUpdateAuthorization.Should().BeFalse();
        legacy.FeedRunning.Should().BeTrue();
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var persisted = await db.PrinterDevices.AsNoTracking()
            .SingleAsync(device => device.DeviceId == deviceId);
        persisted.SupportsUpdateAuthorization.Should().BeFalse();
    }

    private async Task<DeviceSummaryDto> ReadSummaryAsync(string deviceId)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var response = await new GetDevicesQueryHandler(db)
            .Handle(new GetDevicesQuery(), CancellationToken.None);
        response.Success.Should().BeTrue();
        return response.Data!.Single(device => device.DeviceId == deviceId);
    }

    private async Task PostAsync(string deviceId, object body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/devices/heartbeat")
        {
            Content = JsonContent.Create(body)
        };
        request.Headers.Add(DeviceApiKeyHeader, TestPrinterApiKey);
        request.Headers.Add("X-Device-Id", deviceId);
        using var response = await Client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<ApiResponse<bool>>(JsonOptions))!
            .Success.Should().BeTrue();
    }
}
