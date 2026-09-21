using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Modules;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Settings;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

[Collection("Database Lane 3")]
public sealed class OrderRoutingIntegrationTests : IntegrationTestBase
{
    private Guid _productId;

    public OrderRoutingIntegrationTests(DatabaseFixture databaseFixture) : base(databaseFixture) { }

    protected override void ConfigureTestServices(IServiceCollection services)
    {
        services.RemoveAll<ITenantModules>();
        services.AddSingleton<ITenantModules>(new TenantModules(
            Options.Create(new ModuleSettings { Enabled = "core,server,printing", Enforce = true }),
            NullLogger<TenantModules>.Instance));
    }

    [Fact]
    public async Task Released_order_is_queued_for_ready_target_and_projection_is_authoritative()
    {
        var deviceId = await RegisterReadyGeneralDeviceAsync();
        AuthenticateAsRole(UserRole.Server);
        var response = await PostAsJsonAsync("/api/staff/orders", CreateBody(Guid.NewGuid(), true));
        var body = (await ReadResponseAsync<ApiResponse<OrderDto>>(response))!;

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        body.Data!.RoutingStates.Should().HaveCount(2);
        var state = body.Data.RoutingStates!.Single(route => route.Target == DevicePrintTarget.General);
        state.Status.Should().Be(DevicePrintStatus.Queued);
        state.DeviceId.Should().Be(deviceId);

        var projected = await Client.GetFromJsonAsync<ApiResponse<List<OrderRoutingStateDto>>>(
            $"/api/staff/orders/{body.Data.Id}/routing", JsonOptions);
        projected!.Success.Should().BeTrue();
        projected.Data.Should().HaveCount(2);
        projected.Data!.Single(route => route.Target == DevicePrintTarget.General)
            .Id.Should().Be(state.Id);
        projected.Data.Single(route => route.Target == DevicePrintTarget.General)
            .Status.Should().Be(DevicePrintStatus.Queued);
    }

    [Fact]
    public async Task Acknowledgement_is_device_bound_and_stale_transition_cannot_overwrite()
    {
        var deviceId = await RegisterReadyGeneralDeviceAsync();
        AuthenticateAsRole(UserRole.Server);
        var response = await PostAsJsonAsync("/api/staff/orders", CreateBody(Guid.NewGuid(), true));
        var order = (await ReadResponseAsync<ApiResponse<OrderDto>>(response))!.Data!;
        var route = order.RoutingStates!.Single(value => value.Target == DevicePrintTarget.General);

        var wrongDevice = await PostAckAsync("other-device", order.Id, route, DevicePrintStatus.Printed);
        wrongDevice.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        await using (var unchanged = DatabaseFixture.CreateContext())
        {
            var state = await unchanged.OrderRoutingStates.SingleAsync(value => value.Id == route.Id);
            state.Status.Should().Be(DevicePrintStatus.Queued);
            state.Version.Should().Be(1);
        }

        var printed = await PostAckAsync(deviceId, order.Id, route, DevicePrintStatus.Printed);
        printed.StatusCode.Should().Be(HttpStatusCode.OK);

        // A duplicate delivery of the same acknowledgement is a no-op, including the route
        // version used by optimistic concurrency.
        (await PostAckAsync(deviceId, order.Id, route, DevicePrintStatus.Printed))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var stale = await PostAckAsync(deviceId, order.Id, route, DevicePrintStatus.Failed);
        stale.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        await using var verify = DatabaseFixture.CreateContext();
        var final = await verify.OrderRoutingStates.SingleAsync(value => value.Id == route.Id);
        final.Status.Should().Be(DevicePrintStatus.Printed);
        final.Version.Should().Be(2);
    }

    [Fact]
    public async Task Intermediate_order_acknowledgement_is_rejected_before_terminal_ack()
    {
        var deviceId = await RegisterReadyGeneralDeviceAsync();
        var order = await CreateReleasedOrderAsync();
        var route = order.RoutingStates!.Single(value => value.Target == DevicePrintTarget.General);

        (await PostAckAsync(deviceId, order.Id, route, DevicePrintStatus.Received))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);

        await using (var unchanged = DatabaseFixture.CreateContext())
        {
            var state = await unchanged.OrderRoutingStates.SingleAsync(value => value.Id == route.Id);
            state.Status.Should().Be(DevicePrintStatus.Queued);
        }

        (await PostAckAsync(deviceId, order.Id, route, DevicePrintStatus.Printed))
            .StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Unconfigured_order_acknowledgement_is_normalized_to_skipped()
    {
        var deviceId = await RegisterReadyGeneralDeviceAsync();
        var order = await CreateReleasedOrderAsync();
        var route = order.RoutingStates!.Single(value => value.Target == DevicePrintTarget.General);

        (await PostAckAsync(deviceId, order.Id, route, DevicePrintStatus.NotConfigured))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        await using var verify = DatabaseFixture.CreateContext();
        var state = await verify.OrderRoutingStates.SingleAsync(value => value.Id == route.Id);
        state.Status.Should().Be(DevicePrintStatus.Skipped);
    }

    [Fact]
    public async Task Concurrent_duplicate_acknowledgements_are_idempotent()
    {
        var deviceId = await RegisterReadyGeneralDeviceAsync();
        AuthenticateAsRole(UserRole.Server);
        var response = await PostAsJsonAsync("/api/staff/orders", CreateBody(Guid.NewGuid(), true));
        var order = (await ReadResponseAsync<ApiResponse<OrderDto>>(response))!.Data!;
        var route = order.RoutingStates!.Single(value => value.Target == DevicePrintTarget.General);

        AuthenticateAsDevice();
        var results = await Task.WhenAll(
            PostAckAsync(deviceId, order.Id, route, DevicePrintStatus.Printed, authenticate: false),
            PostAckAsync(deviceId, order.Id, route, DevicePrintStatus.Printed, authenticate: false));

        var resultDetails = await Task.WhenAll(results.Select(async result =>
            $"{result.StatusCode}: {await result.Content.ReadAsStringAsync()}"));
        results.Select(result => result.StatusCode)
            .Should().OnlyContain(status => status == HttpStatusCode.OK, string.Join(" | ", resultDetails));
        await using var verify = DatabaseFixture.CreateContext();
        var state = await verify.OrderRoutingStates.SingleAsync(value => value.Id == route.Id);
        state.Status.Should().Be(DevicePrintStatus.Printed);
        state.Version.Should().Be(2);
        (await verify.DeviceOrderReceipts.CountAsync(receipt =>
            receipt.DeviceId == deviceId && receipt.JobId == route.JobId)).Should().Be(1);
    }

    [Fact]
    public async Task Unknown_order_job_acknowledgement_is_rejected_without_receipt()
    {
        var deviceId = await RegisterReadyGeneralDeviceAsync();
        AuthenticateAsRole(UserRole.Server);
        var response = await PostAsJsonAsync("/api/staff/orders", CreateBody(Guid.NewGuid(), true));
        var order = (await ReadResponseAsync<ApiResponse<OrderDto>>(response))!.Data!;
        var route = order.RoutingStates!.Single(value => value.Target == DevicePrintTarget.General);
        var unknown = route with { JobId = Guid.NewGuid() };

        var acknowledgement = await PostAckAsync(deviceId, order.Id, unknown, DevicePrintStatus.Printed);

        acknowledgement.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        await using var verify = DatabaseFixture.CreateContext();
        (await verify.DeviceOrderReceipts.CountAsync(receipt => receipt.OrderId == order.Id))
            .Should().Be(0);
    }

    [Fact]
    public async Task Stale_feed_heartbeat_downgrades_queued_route_to_not_configured()
    {
        var deviceId = await RegisterReadyGeneralDeviceAsync();
        AuthenticateAsRole(UserRole.Server);
        var response = await PostAsJsonAsync("/api/staff/orders", CreateBody(Guid.NewGuid(), true));
        var order = (await ReadResponseAsync<ApiResponse<OrderDto>>(response))!.Data!;
        order.RoutingStates!.Single(value => value.Target == DevicePrintTarget.General)
            .Status.Should().Be(DevicePrintStatus.Queued);

        await PostHeartbeatAsync(deviceId, DateTime.UtcNow.AddMinutes(-10));
        var projection = await Client.GetFromJsonAsync<ApiResponse<List<OrderRoutingStateDto>>>(
            $"/api/staff/orders/{order.Id}/routing", JsonOptions);

        await using (var check = DatabaseFixture.CreateContext())
        {
            (await check.OrderRoutingStates.CountAsync(state => state.OrderId == order.Id))
                .Should().Be(2);
        }
        projection!.Data.Should().HaveCount(2);
        projection.Data!.Single(value => value.Target == DevicePrintTarget.General)
            .Status.Should().Be(DevicePrintStatus.NotConfigured);
        projection.Data.Single(value => value.Target == DevicePrintTarget.General)
            .DeviceId.Should().BeNull();
    }

    [Fact]
    public async Task Projection_backfills_active_released_order_without_route_rows()
    {
        var deviceId = await RegisterReadyGeneralDeviceAsync();
        AuthenticateAsRole(UserRole.Server);
        var response = await PostAsJsonAsync("/api/staff/orders", CreateBody(Guid.NewGuid(), false));
        var order = (await ReadResponseAsync<ApiResponse<OrderDto>>(response))!.Data!;

        await using (var context = DatabaseFixture.CreateContext())
        {
            var persisted = await context.Orders.SingleAsync(item => item.Id == order.Id);
            persisted.IsKitchenReleased = true;
            persisted.Status = OrderStatus.Confirmed;
            await context.SaveChangesAsync();
            (await context.Orders.Where(item => item.Id == order.Id)
                .Select(item => new { item.IsKitchenReleased, item.Status })
                .SingleAsync()).IsKitchenReleased.Should().BeTrue();
        }

        var projection = await Client.GetFromJsonAsync<ApiResponse<List<OrderRoutingStateDto>>>(
            $"/api/staff/orders/{order.Id}/routing", JsonOptions);

        projection!.Data.Should().HaveCount(2);
        projection.Data!.Single(value => value.Target == DevicePrintTarget.General)
            .DeviceId.Should().Be(deviceId);
        projection.Data.Single(value => value.Target == DevicePrintTarget.General)
            .Status.Should().Be(DevicePrintStatus.Queued);
    }

    [Fact]
    public async Task Device_feed_backfills_pre_migration_released_order_before_routing_filter()
    {
        var deviceId = await RegisterReadyGeneralDeviceAsync();
        AuthenticateAsRole(UserRole.Server);
        var response = await PostAsJsonAsync("/api/staff/orders", CreateBody(Guid.NewGuid(), false));
        var order = (await ReadResponseAsync<ApiResponse<OrderDto>>(response))!.Data!;

        await using (var context = DatabaseFixture.CreateContext())
        {
            var persisted = await context.Orders.SingleAsync(item => item.Id == order.Id);
            persisted.IsKitchenReleased = true;
            persisted.Status = OrderStatus.Confirmed;
            await context.SaveChangesAsync();
            (await context.OrderRoutingStates.CountAsync(state => state.OrderId == order.Id))
                .Should().Be(0);
        }

        var feed = await FetchPrinterFeedAsync(deviceId);
        OrderNumbers(feed).Should().ContainSingle(order.OrderNumber);
        RoutesFor(feed, order.OrderNumber).Should().HaveCount(2)
            .And.OnlyContain(route => route!["deviceId"]!.GetValue<string>() == deviceId);

        await using var verify = DatabaseFixture.CreateContext();
        (await verify.OrderRoutingStates.CountAsync(state => state.OrderId == order.Id))
            .Should().Be(2);
    }

    [Fact]
    public async Task Capabilityless_legacy_device_is_not_assigned_durable_routes()
    {
        var legacyDeviceId = "legacy-" + Guid.NewGuid().ToString("N");
        await RegisterLegacyDeviceAsync(legacyDeviceId);
        var order = await CreateReleasedOrderAsync();

        order.RoutingStates.Should().HaveCount(2)
            .And.OnlyContain(route => route.DeviceId == null
                && route.Status == DevicePrintStatus.NotConfigured);

        var legacyFeed = await FetchPrinterFeedAsync();
        OrderNumbers(legacyFeed).Should().Contain(order.OrderNumber);
        legacyFeed["data"]!["items"]!.AsArray()
            .Single(item => item!["orderNumber"]!.GetValue<string>() == order.OrderNumber)
            !["routingStates"].Should().BeNull();
    }

    [Fact]
    public async Task Device_feed_includes_only_assigned_routes_and_legacy_poll_keeps_broadcast_compatibility()
    {
        var firstDevice = await RegisterReadyGeneralDeviceAsync();
        var firstOrder = await CreateReleasedOrderAsync();

        var secondDevice = "routing-second-" + Guid.NewGuid().ToString("N");
        await RegisterDeviceAsync(secondDevice, "SingleKitchen", "Cashier", "General");
        var secondOrder = await CreateReleasedOrderAsync();

        var firstFeed = await FetchPrinterFeedAsync(firstDevice);
        var secondFeed = await FetchPrinterFeedAsync(secondDevice);
        var legacyFeed = await FetchPrinterFeedAsync();

        OrderNumbers(firstFeed).Should().ContainSingle(firstOrder.OrderNumber);
        OrderNumbers(firstFeed).Should().NotContain(secondOrder.OrderNumber);
        OrderNumbers(secondFeed).Should().ContainSingle(secondOrder.OrderNumber);
        OrderNumbers(secondFeed).Should().NotContain(firstOrder.OrderNumber);
        OrderNumbers(legacyFeed).Should().Contain(firstOrder.OrderNumber)
            .And.Contain(secondOrder.OrderNumber);

        RoutesFor(firstFeed, firstOrder.OrderNumber).Should().OnlyContain(route =>
            route!["deviceId"]!.GetValue<string>() == firstDevice);
        RoutesFor(secondFeed, secondOrder.OrderNumber).Should().OnlyContain(route =>
            route!["deviceId"]!.GetValue<string>() == secondDevice);

        foreach (var route in firstOrder.RoutingStates!.Where(state => state.DeviceId == firstDevice))
        {
            (await PostAckAsync(firstDevice, firstOrder.Id, route, DevicePrintStatus.Printed))
                .StatusCode.Should().Be(HttpStatusCode.OK);
        }

        var replay = await FetchPrinterFeedAsync(firstDevice);
        OrderNumbers(replay).Should().NotContain(firstOrder.OrderNumber,
            "terminal route states must not be replayed as printable jobs");
    }

    [Fact]
    public async Task Device_feed_does_not_redeliver_received_sent_or_unknown_routes()
    {
        var deviceId = await RegisterReadyGeneralDeviceAsync();
        var order = await CreateReleasedOrderAsync();

        foreach (var status in new[]
                 {
                     DevicePrintStatus.Received,
                     DevicePrintStatus.Sent,
                     DevicePrintStatus.Unknown,
                     DevicePrintStatus.Failed,
                     DevicePrintStatus.Skipped,
                     DevicePrintStatus.Printed
                 })
        {
            await using (var context = DatabaseFixture.CreateContext())
            {
                var routes = await context.OrderRoutingStates
                    .Where(route => route.OrderId == order.Id)
                    .ToListAsync();
                routes.Should().HaveCount(2);
                foreach (var route in routes)
                {
                    route.Status = status;
                }

                await context.SaveChangesAsync();
            }

            var feed = await FetchPrinterFeedAsync(deviceId);
            OrderNumbers(feed).Should().NotContain(order.OrderNumber,
                $"{status} is not an automatic replay-safe delivery state");
        }
    }

    [Fact]
    public async Task Device_feed_reconciles_not_configured_routes_after_offline_release()
    {
        var order = await CreateReleasedOrderAsync();
        order.RoutingStates.Should().OnlyContain(route =>
            route.Status == DevicePrintStatus.NotConfigured && route.DeviceId == null);

        var deviceId = "routing-recovered-" + Guid.NewGuid().ToString("N");
        await RegisterDeviceAsync(deviceId, "SingleKitchen", "Cashier", "General");

        var feed = await FetchPrinterFeedAsync(deviceId);
        OrderNumbers(feed).Should().ContainSingle(order.OrderNumber);
        RoutesFor(feed, order.OrderNumber).Should().HaveCount(2)
            .And.OnlyContain(route => route!["deviceId"]!.GetValue<string>() == deviceId
                && route["status"]!.GetValue<string>() == nameof(DevicePrintStatus.Queued));
    }

    [Fact]
    public async Task Device_feed_rejects_unknown_identity_but_missing_identity_keeps_legacy_orders()
    {
        var order = await CreateReleasedOrderAsync();

        var unknown = await FetchPrinterFeedAsync("never-registered-device");
        unknown["success"]!.GetValue<bool>().Should().BeFalse();
        unknown["message"]!.GetValue<string>().Should().NotContain("not registered");
        unknown["data"]!["items"]!.AsArray().Should().BeEmpty();

        var empty = await FetchPrinterFeedAsync("   ");
        empty["success"]!.GetValue<bool>().Should().BeFalse();
        empty["message"]!.GetValue<string>().Should().NotContain("header cannot be empty");

        var legacy = await FetchPrinterFeedAsync();
        legacy["success"]!.GetValue<bool>().Should().BeTrue();
        OrderNumbers(legacy).Should().ContainSingle(order.OrderNumber);
    }

    [Fact]
    public async Task Device_feed_exposes_default_station_and_general_single_kitchen_targets()
    {
        var deviceId = "routing-mode-" + Guid.NewGuid().ToString("N");
        await RegisterDeviceAsync(deviceId, "Stations", "Cashier", "Default");
        var stationOrder = await CreateReleasedOrderAsync();

        var generalDeviceId = "routing-general-" + Guid.NewGuid().ToString("N");
        await RegisterDeviceAsync(generalDeviceId, "SingleKitchen", "Cashier", "General");
        var generalOrder = await CreateReleasedOrderAsync();
        var stationFeed = await FetchPrinterFeedAsync(deviceId);
        var generalFeed = await FetchPrinterFeedAsync(generalDeviceId);

        RoutesFor(stationFeed, stationOrder.OrderNumber)
            .Select(route => route!["target"]!.GetValue<string>())
            .Should().Contain(nameof(DevicePrintTarget.Default));
        RoutesFor(generalFeed, generalOrder.OrderNumber)
            .Select(route => route!["target"]!.GetValue<string>())
            .Should().Contain(nameof(DevicePrintTarget.General));
    }

    private async Task<OrderDto> CreateReleasedOrderAsync()
    {
        AuthenticateAsRole(UserRole.Server);
        var response = await PostAsJsonAsync("/api/staff/orders", CreateBody(Guid.NewGuid(), true));
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = (await ReadResponseAsync<ApiResponse<OrderDto>>(response))!;
        body.Success.Should().BeTrue();
        return body.Data!;
    }

    private async Task<JsonNode> FetchPrinterFeedAsync(string? deviceId = null)
    {
        AuthenticateAsDevice();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/orders/printer-feed");
        if (deviceId is not null)
        {
            request.Headers.Add("X-Device-Id", deviceId);
        }

        var response = await Client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
    }

    private static IEnumerable<string> OrderNumbers(JsonNode feed) =>
        feed["data"]!["items"]!.AsArray()
            .Select(order => order!["orderNumber"]!.GetValue<string>());

    private static JsonArray RoutesFor(JsonNode feed, string orderNumber) =>
        feed["data"]!["items"]!.AsArray()
            .Single(order => order!["orderNumber"]!.GetValue<string>() == orderNumber)!["routingStates"]!
            .AsArray();

    private async Task<string> RegisterReadyGeneralDeviceAsync()
    {
        var deviceId = "routing-" + Guid.NewGuid().ToString("N");
        await RegisterDeviceAsync(deviceId, "SingleKitchen", "Cashier", "General");
        return deviceId;
    }

    private async Task RegisterDeviceAsync(
        string deviceId, string routingMode, params string[] targets)
    {
        AuthenticateAsDevice();
        var capabilities = targets.Select(target => (object)new
        {
            target,
            isSupported = true,
            isConfigured = true,
            autoPrintEnabled = true,
            printerName = target.ToLowerInvariant()
        }).ToArray();
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/devices/heartbeat")
        {
            Content = JsonContent.Create(new
            {
                feedRunning = true,
                lastSuccessfulPollAt = DateTime.UtcNow,
                kitchenRoutingMode = routingMode,
                kitchenPrinter = "general:9100",
                targetCapabilities = capabilities
            })
        };
        request.Headers.Add("X-Device-Id", deviceId);
        (await Client.SendAsync(request)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private async Task RegisterLegacyDeviceAsync(string deviceId)
    {
        AuthenticateAsDevice();
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/devices/heartbeat")
        {
            Content = JsonContent.Create(new
            {
                feedRunning = true,
                lastSuccessfulPollAt = DateTime.UtcNow,
                kitchenPrinter = "legacy:9100"
            })
        };
        request.Headers.Add("X-Device-Id", deviceId);
        (await Client.SendAsync(request)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private async Task<HttpResponseMessage> PostAckAsync(
        string deviceId, Guid orderId, OrderRoutingStateDto route, DevicePrintStatus status,
        bool authenticate = true)
    {
        if (authenticate)
        {
            AuthenticateAsDevice();
        }
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/devices/print-acks")
        {
            Content = JsonContent.Create(new
            {
                acks = new[]
                {
                    new
                    {
                        orderId,
                        target = route.Target.ToString(),
                        status = status.ToString(),
                        receivedAt = DateTime.UtcNow,
                        printedAt = status == DevicePrintStatus.Printed ? DateTime.UtcNow : (DateTime?)null,
                        failureReason = status == DevicePrintStatus.Failed ? "test" : null,
                        copies = 1,
                        jobId = route.JobId,
                        revision = route.Revision,
                        jobType = "Order"
                    }
                }
            })
        };
        request.Headers.Add("X-Device-Id", deviceId);
        return await Client.SendAsync(request);
    }

    private async Task<HttpResponseMessage> PostHeartbeatAsync(string deviceId, DateTime pollAt)
    {
        AuthenticateAsDevice();
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/devices/heartbeat")
        {
            Content = JsonContent.Create(new
            {
                feedRunning = true,
                lastSuccessfulPollAt = pollAt,
                kitchenRoutingMode = "SingleKitchen",
                targetCapabilities = new[]
                {
                    new { target = "Cashier", isSupported = true, isConfigured = true,
                        autoPrintEnabled = true, printerName = "cashier" },
                    new { target = "General", isSupported = true, isConfigured = true,
                        autoPrintEnabled = true, printerName = "general" }
                }
            })
        };
        request.Headers.Add("X-Device-Id", deviceId);
        return await Client.SendAsync(request);
    }

    private object CreateBody(Guid operationId, bool releaseToKitchen) => new
    {
        clientOperationId = operationId,
        releaseToKitchen,
        type = nameof(OrderType.Takeaway),
        paymentState = nameof(StaffOrderPaymentState.Unpaid),
        items = new[] { new { productId = _productId, quantity = 1 } }
    };

    protected override async Task SeedTestData()
    {
        await base.SeedTestData();
        await using var context = DatabaseFixture.CreateContext();
        _productId = await context.Products
            .Where(product => product.Name == "Test Pizza")
            .Select(product => product.Id)
            .SingleAsync();
    }
}
