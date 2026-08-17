using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Infrastructure;
using Xunit;

namespace RestaurantSystem.IntegrationTests.Features.Devices;

[Collection("Database Lane 4")]
public class DeviceTelemetryTests : IntegrationTestBase
{
    public DeviceTelemetryTests(DatabaseFixture databaseFixture) : base(databaseFixture) { }

    private static string NewDeviceId() => "dev-" + Guid.NewGuid().ToString("N");

    private async Task<HttpResponseMessage> PostAsync(string path, object body, string? deviceId)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = JsonContent.Create(body),
        };
        if (deviceId is not null)
            request.Headers.Add("X-Device-Id", deviceId);
        return await Client.SendAsync(request);
    }

    private ApplicationDbContext Db(IServiceScope scope) =>
        scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

    // ---- print-acks ---------------------------------------------------------

    [Fact]
    public async Task PrintAcks_WithDeviceId_PersistsReceipts()
    {
        var deviceId = NewDeviceId();
        var orderId = Guid.NewGuid();
        var body = new
        {
            acks = new[]
            {
                new
                {
                    orderId,
                    target = "FrontKitchen",
                    status = "Printed",
                    receivedAt = DateTime.UtcNow,
                    printedAt = DateTime.UtcNow,
                    failureReason = (string?)null,
                    copies = 1,
                },
            },
        };

        var response = await PostAsync("/api/devices/print-acks", body, deviceId);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await response.Content.ReadFromJsonAsync<ApiResponse<bool>>(JsonOptions);
        payload!.Data.Should().BeTrue();

        using var scope = Factory.Services.CreateScope();
        var receipt = Db(scope).DeviceOrderReceipts
            .Single(r => r.DeviceId == deviceId && r.OrderId == orderId);
        receipt.Target.Should().Be(DevicePrintTarget.FrontKitchen);
        receipt.Status.Should().Be(DevicePrintStatus.Printed);
        receipt.Copies.Should().Be(1);
        receipt.PrintedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task PrintAcks_SameOrderTargetTwice_UpsertsAndUpdatesStatus()
    {
        var deviceId = NewDeviceId();
        var orderId = Guid.NewGuid();

        object Ack(string status) => new
        {
            acks = new[]
            {
                new
                {
                    orderId,
                    target = "Cashier",
                    status,
                    receivedAt = DateTime.UtcNow,
                    printedAt = (DateTime?)null,
                    failureReason = (string?)null,
                    copies = 1,
                },
            },
        };

        (await PostAsync("/api/devices/print-acks", Ack("Received"), deviceId))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await PostAsync("/api/devices/print-acks", Ack("Printed"), deviceId))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        using var scope = Factory.Services.CreateScope();
        var receipts = Db(scope).DeviceOrderReceipts
            .Where(r => r.DeviceId == deviceId && r.OrderId == orderId).ToList();
        receipts.Should().ContainSingle();
        receipts[0].Status.Should().Be(DevicePrintStatus.Printed);
    }

    [Fact]
    public async Task PrintAcks_MissingDeviceIdHeader_ReturnsBadRequest()
    {
        var body = new { acks = new[] { new { orderId = Guid.NewGuid(), target = "Cashier", status = "Printed", receivedAt = DateTime.UtcNow, printedAt = (DateTime?)null, failureReason = (string?)null, copies = 1 } } };

        var response = await PostAsync("/api/devices/print-acks", body, deviceId: null);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task PrintAcks_OutOfRangeTargetEnum_ReturnsBadRequest()
    {
        // Enums persist as strings; an out-of-range value must be rejected (not stored as "0"/"99").
        var body = new { acks = new[] { new { orderId = Guid.NewGuid(), target = 99, status = "Printed", receivedAt = DateTime.UtcNow, printedAt = (DateTime?)null, failureReason = (string?)null, copies = 1 } } };

        var response = await PostAsync("/api/devices/print-acks", body, NewDeviceId());

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ---- events -------------------------------------------------------------

    [Fact]
    public async Task Events_WithDeviceId_PersistsEvents()
    {
        var deviceId = NewDeviceId();
        var body = new
        {
            events = new[]
            {
                new
                {
                    clientEventId = Guid.NewGuid().ToString("N"),
                    occurredAt = DateTime.UtcNow,
                    level = "Error",
                    code = "PRINTER_UNREACHABLE",
                    message = "Kitchen printer did not respond",
                    context = "{\"target\":\"FrontKitchen\"}",
                },
            },
        };

        var response = await PostAsync("/api/devices/events", body, deviceId);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var scope = Factory.Services.CreateScope();
        var evt = Db(scope).DeviceEvents.Single(e => e.DeviceId == deviceId);
        evt.Level.Should().Be(DeviceEventLevel.Error);
        evt.Code.Should().Be("PRINTER_UNREACHABLE");
        evt.Context.Should().NotBeNull();
    }

    [Fact]
    public async Task Events_DuplicateClientEventId_IsNotDoubleInserted()
    {
        var deviceId = NewDeviceId();
        var clientEventId = Guid.NewGuid().ToString("N");
        object Batch() => new
        {
            events = new[]
            {
                new
                {
                    clientEventId,
                    occurredAt = DateTime.UtcNow,
                    level = "Warning",
                    code = (string?)null,
                    message = "Feed idle",
                    context = (string?)null,
                },
            },
        };

        (await PostAsync("/api/devices/events", Batch(), deviceId)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await PostAsync("/api/devices/events", Batch(), deviceId)).StatusCode.Should().Be(HttpStatusCode.OK);

        using var scope = Factory.Services.CreateScope();
        Db(scope).DeviceEvents.Count(e => e.DeviceId == deviceId && e.ClientEventId == clientEventId)
            .Should().Be(1);
    }

    [Fact]
    public async Task Events_NonJsonContext_IsAcceptedNotWedged()
    {
        // Context is stored as plain text, not jsonb, precisely so a malformed value can't hard-fail
        // the insert and wedge a retrying outbox. A non-JSON string must persist, not 500.
        var deviceId = NewDeviceId();
        var body = new
        {
            events = new[]
            {
                new
                {
                    clientEventId = Guid.NewGuid().ToString("N"),
                    occurredAt = DateTime.UtcNow,
                    level = "Error",
                    code = (string?)null,
                    message = "crash",
                    context = "not-json: at StackFrame line 42",
                },
            },
        };

        var response = await PostAsync("/api/devices/events", body, deviceId);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var scope = Factory.Services.CreateScope();
        Db(scope).DeviceEvents.Single(e => e.DeviceId == deviceId)
            .Context.Should().Be("not-json: at StackFrame line 42");
    }

    [Fact]
    public async Task Events_MissingDeviceIdHeader_ReturnsBadRequest()
    {
        var body = new { events = new[] { new { clientEventId = "x", occurredAt = DateTime.UtcNow, level = "Info", code = (string?)null, message = "hi", context = (string?)null } } };

        var response = await PostAsync("/api/devices/events", body, deviceId: null);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Events_ZonelessTimestamp_IsAcceptedNotRejectedByTimestamptz()
    {
        // A zoneless ISO timestamp deserialises to Kind=Unspecified; the handler must normalise to
        // UTC or Npgsql rejects the write to the `timestamptz` column (500).
        var deviceId = NewDeviceId();
        var body = new
        {
            events = new[]
            {
                new
                {
                    clientEventId = Guid.NewGuid().ToString("N"),
                    occurredAt = "2026-07-19T10:00:00",
                    level = "Info",
                    code = (string?)null,
                    message = "startup",
                    context = (string?)null,
                },
            },
        };

        var response = await PostAsync("/api/devices/events", body, deviceId);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var scope = Factory.Services.CreateScope();
        (await Db(scope).DeviceEvents.AnyAsync(e => e.DeviceId == deviceId)).Should().BeTrue();
    }
}
