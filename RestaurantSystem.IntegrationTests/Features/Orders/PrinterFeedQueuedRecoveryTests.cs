using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Common.Modules;
using RestaurantSystem.Api.Settings;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

public abstract class PrinterFeedRecoveryTestBase : IntegrationTestBase
{
    private static readonly DateTimeOffset FixedNow = DateTimeOffset.FromUnixTimeMilliseconds(
        DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
    protected readonly MutableTimeProvider TestTimeProvider = new(FixedNow);

    protected PrinterFeedRecoveryTestBase(DatabaseFixture databaseFixture) : base(databaseFixture) { }

    protected abstract int RecoveryWindowHours { get; }

    protected static DateTime Now => FixedNow.UtcDateTime;

    protected override void ConfigureTestServices(IServiceCollection services)
    {
        services.RemoveAll<ITenantModules>();
        services.AddSingleton<ITenantModules>(new TenantModules(
            Options.Create(new ModuleSettings { Enabled = "core,server,printing", Enforce = true }),
            NullLogger<TenantModules>.Instance));
        services.PostConfigure<OrderRoutingSettings>(settings =>
        {
            settings.ProcessingBatchSize = 2;
            settings.RequiredQueuedRouteRecoveryHours = RecoveryWindowHours;
        });
        services.RemoveAll<TimeProvider>();
        services.AddSingleton<TimeProvider>(TestTimeProvider);
    }

    protected async Task RegisterReadyDeviceAsync(string deviceId)
    {
        AuthenticateAsDevice();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/devices/heartbeat")
        {
            Content = JsonContent.Create(new
            {
                feedRunning = true,
                lastSuccessfulPollAt = Now,
                kitchenRoutingMode = "SingleKitchen",
                kitchenPrinter = "general:9100",
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
        (await Client.SendAsync(request)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    protected async Task<SeededOrder> SeedOrderAsync(
        string orderNumber, DateTime createdAt, params RouteSeed[] routes)
    {
        var order = new Order
        {
            Id = Guid.NewGuid(),
            OrderNumber = orderNumber,
            Type = OrderType.Takeaway,
            Status = OrderStatus.Confirmed,
            PaymentStatus = PaymentStatus.Pending,
            IsKitchenReleased = true,
            OrderDate = createdAt,
            CreatedAt = createdAt,
            CreatedBy = "printer-recovery-test"
        };

        var seededRoutes = new List<SeededRoute>();
        foreach (var route in routes)
        {
            var state = new OrderRoutingState
            {
                Id = Guid.NewGuid(),
                OrderId = order.Id,
                JobId = Guid.NewGuid(),
                Revision = 1,
                Version = 1,
                Target = route.Target,
                IsRequired = route.IsRequired,
                Status = route.Status,
                DeviceId = route.DeviceId,
                CreatedAt = route.CreatedAt ?? createdAt,
                CreatedBy = "printer-recovery-test"
            };
            order.RoutingStates.Add(state);
            seededRoutes.Add(new SeededRoute(state.Id, state.JobId, state.Target, state.IsRequired));
        }

        await using var context = DatabaseFixture.CreateContext();
        context.Orders.Add(order);
        await context.SaveChangesAsync();
        return new SeededOrder(order.Id, orderNumber, seededRoutes);
    }

    protected async Task<JsonNode> FetchFeedAsync(
        string? deviceId, DateTime modifiedSince, string? orderCursor = null)
    {
        AuthenticateAsDevice();
        var query = new List<string>
        {
            "modifiedSince=" + Uri.EscapeDataString(modifiedSince.ToString("O"))
        };
        if (orderCursor is not null)
            query.Add("orderCursor=" + Uri.EscapeDataString(orderCursor));

        using var request = new HttpRequestMessage(
            HttpMethod.Get, "/api/orders/printer-feed?" + string.Join("&", query));
        if (deviceId is not null)
            request.Headers.Add("X-Device-Id", deviceId);
        var response = await Client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
    }

    protected static JsonArray Items(JsonNode feed) => feed["data"]!["items"]!.AsArray();

    protected static IEnumerable<string> OrderNumbers(JsonNode feed) => Items(feed)
        .Select(order => order!["orderNumber"]!.GetValue<string>());

    protected static JsonNode Route(JsonNode feed, string orderNumber, DevicePrintTarget target) => Items(feed)
        .Single(order => order!["orderNumber"]!.GetValue<string>() == orderNumber)!["routingStates"]!
        .AsArray().Single(route => route!["target"]!.GetValue<string>() == target.ToString())!;

    protected async Task<HttpResponseMessage> PrintAckAsync(
        string deviceId, Guid orderId, JsonNode route)
    {
        AuthenticateAsDevice();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/devices/print-acks")
        {
            Content = JsonContent.Create(new
            {
                acks = new[]
                {
                    new
                    {
                        orderId,
                        target = route["target"]!.GetValue<string>(),
                        status = nameof(DevicePrintStatus.Printed),
                        receivedAt = Now,
                        printedAt = Now,
                        failureReason = (string?)null,
                        copies = 1,
                        jobId = route["jobId"]!.GetValue<Guid>(),
                        revision = route["revision"]!.GetValue<int>(),
                        jobType = "Order"
                    }
                }
            })
        };
        request.Headers.Add("X-Device-Id", deviceId);
        return await Client.SendAsync(request);
    }

    protected async Task ClearOrderUpdateAsync(Guid orderId)
    {
        await using var context = DatabaseFixture.CreateContext();
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE orders SET updated_at=NULL WHERE id={orderId}");
    }

    protected sealed record RouteSeed(
        DevicePrintTarget Target,
        bool IsRequired,
        DevicePrintStatus Status,
        string? DeviceId,
        DateTime? CreatedAt = null);

    protected sealed record SeededRoute(Guid Id, Guid JobId, DevicePrintTarget Target, bool IsRequired);

    protected sealed record SeededOrder(Guid Id, string OrderNumber, IReadOnlyList<SeededRoute> Routes);

    protected sealed class MutableTimeProvider(DateTimeOffset initialNow) : TimeProvider
    {
        private DateTimeOffset _now = initialNow;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan amount) => _now = _now.Add(amount);
    }
}

[Collection("Database Lane 3")]
public sealed class PrinterFeedQueuedRecoveryTests : PrinterFeedRecoveryTestBase
{
    public PrinterFeedQueuedRecoveryTests(DatabaseFixture databaseFixture) : base(databaseFixture) { }

    protected override int RecoveryWindowHours => 1;

    [Theory]
    [InlineData(59, true)]
    [InlineData(60, true)]
    [InlineData(61, false)]
    [InlineData(180, false)]
    public async Task Required_queued_routes_are_recovered_only_inside_the_configured_default_window(
        int ageMinutes, bool expectedInFeed)
    {
        var deviceId = "stale-queue-" + Guid.NewGuid().ToString("N");
        await RegisterReadyDeviceAsync(deviceId);
        var orderNumber = "RECOVERY-AGE-" + ageMinutes;
        var routeTime = Now.AddMinutes(-ageMinutes);
        var order = await SeedOrderAsync(orderNumber, routeTime,
            new RouteSeed(DevicePrintTarget.General, true, DevicePrintStatus.Queued, deviceId));

        var feed = await FetchFeedAsync(deviceId, Now.AddMinutes(-5));

        if (expectedInFeed)
        {
            OrderNumbers(feed).Should().Contain(orderNumber);
            Route(feed, orderNumber, DevicePrintTarget.General)["isRequired"]!.GetValue<bool>().Should().BeTrue();
        }
        else
        {
            OrderNumbers(feed).Should().NotContain(orderNumber);
        }

        await using var context = DatabaseFixture.CreateContext();
        var unchanged = await context.OrderRoutingStates.SingleAsync(state => state.Id == order.Routes[0].Id);
        unchanged.JobId.Should().Be(order.Routes[0].JobId);
        unchanged.Status.Should().Be(DevicePrintStatus.Queued);
        unchanged.Version.Should().Be(1);
        unchanged.CreatedAt.Should().Be(routeTime);
        unchanged.UpdatedAt.Should().BeNull();
    }

    [Fact]
    public async Task Aged_delivery_is_device_scoped_suppressed_for_legacy_callers_and_stops_after_ack()
    {
        var deviceId = "stale-queue-owner-" + Guid.NewGuid().ToString("N");
        var otherDeviceId = "stale-queue-other-" + Guid.NewGuid().ToString("N");
        await RegisterReadyDeviceAsync(deviceId);
        await RegisterReadyDeviceAsync(otherDeviceId);
        var routeTime = Now.AddMinutes(-45);
        var order = await SeedOrderAsync("RECOVERY-ACK", routeTime,
            new RouteSeed(DevicePrintTarget.General, true, DevicePrintStatus.Queued, deviceId),
            new RouteSeed(DevicePrintTarget.Cashier, false, DevicePrintStatus.Queued, deviceId));
        var since = Now.AddMinutes(-5);

        var ownerFeed = await FetchFeedAsync(deviceId, since);
        var requiredRoute = Route(ownerFeed, order.OrderNumber, DevicePrintTarget.General);
        requiredRoute["status"]!.GetValue<string>().Should().Be(nameof(DevicePrintStatus.Queued));
        requiredRoute["jobId"]!.GetValue<Guid>().Should().Be(order.Routes[0].JobId);
        (await FetchFeedAsync(deviceId, since))["data"]!["items"]!.AsArray()
            .Select(item => item!["orderNumber"]!.GetValue<string>())
            .Should().Contain(order.OrderNumber);
        OrderNumbers(await FetchFeedAsync(otherDeviceId, since)).Should().NotContain(order.OrderNumber);
        OrderNumbers(await FetchFeedAsync(null, since)).Should().NotContain(order.OrderNumber);

        (await PrintAckAsync(deviceId, order.Id, requiredRoute)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await PrintAckAsync(deviceId, order.Id, requiredRoute)).StatusCode.Should().Be(HttpStatusCode.OK);
        await ClearOrderUpdateAsync(order.Id);

        // The optional cashier route is still queued, but it cannot keep this stale order alive.
        OrderNumbers(await FetchFeedAsync(deviceId, since)).Should().NotContain(order.OrderNumber);
        await using var context = DatabaseFixture.CreateContext();
        var routes = await context.OrderRoutingStates.Where(state => state.OrderId == order.Id)
            .OrderBy(state => state.Target).ToListAsync();
        routes.Single(state => state.Target == DevicePrintTarget.General).Status.Should()
            .Be(DevicePrintStatus.Printed);
        routes.Single(state => state.Target == DevicePrintTarget.General).JobId.Should()
            .Be(order.Routes[0].JobId);
        routes.Single(state => state.Target == DevicePrintTarget.Cashier).Status.Should()
            .Be(DevicePrintStatus.Queued);
    }

    [Fact]
    public async Task Old_optional_queued_route_alone_does_not_bypass_the_cursor()
    {
        var deviceId = "stale-optional-" + Guid.NewGuid().ToString("N");
        await RegisterReadyDeviceAsync(deviceId);
        var routeTime = Now.AddHours(-1);
        var order = await SeedOrderAsync("RECOVERY-OPTIONAL", routeTime,
            new RouteSeed(DevicePrintTarget.General, true, DevicePrintStatus.Printed, deviceId),
            new RouteSeed(DevicePrintTarget.Cashier, false, DevicePrintStatus.Queued, deviceId));

        OrderNumbers(await FetchFeedAsync(deviceId, Now.AddMinutes(-5))).Should()
            .NotContain(order.OrderNumber);
    }

    [Fact]
    public async Task Aged_required_work_is_drained_across_stable_bounded_order_pages()
    {
        const int orderCount = 51;
        var deviceId = "stale-page-" + Guid.NewGuid().ToString("N");
        await RegisterReadyDeviceAsync(deviceId);
        var cutoff = Now.AddHours(-1);
        var orders = new List<SeededOrder>();
        for (var index = 0; index < orderCount; index++)
        {
            orders.Add(await SeedOrderAsync($"RECOVERY-PAGE-{index:D2}", cutoff,
                new RouteSeed(DevicePrintTarget.General, true, DevicePrintStatus.Queued, deviceId)));
        }

        var firstRepeat = await FetchFeedAsync(deviceId, Now.AddMinutes(-5));
        var secondRepeat = await FetchFeedAsync(deviceId, Now.AddMinutes(-5));
        OrderNumbers(secondRepeat).Should().Equal(OrderNumbers(firstRepeat));
        secondRepeat["data"]!["nextOrderCursor"]!.GetValue<string>()
            .Should().Be(firstRepeat["data"]!["nextOrderCursor"]!.GetValue<string>());

        var firstPage = firstRepeat;
        var seen = new List<string>(OrderNumbers(firstPage));
        var firstData = firstPage["data"]!;
        firstData["hasMoreOrders"]!.GetValue<bool>().Should().BeTrue();
        var cursor = firstData["nextOrderCursor"]!.GetValue<string>();

        // The second request moves past the inclusive route-time boundary. It must use the
        // cutoff frozen into the cursor instead of recomputing a newer one.
        TestTimeProvider.Advance(TimeSpan.FromSeconds(1));
        var secondPage = await FetchFeedAsync(deviceId, Now.AddMinutes(-5), cursor);
        Items(secondPage).Count.Should().BeLessThanOrEqualTo(50);
        seen.AddRange(OrderNumbers(secondPage));

        secondPage["data"]!["hasMoreOrders"]!.GetValue<bool>().Should().BeFalse();
        seen.Should().OnlyHaveUniqueItems().And.BeEquivalentTo(orders.Select(order => order.OrderNumber));
    }

    [Fact]
    public async Task Order_cursor_rejects_forged_recovery_fences_and_expired_continuations()
    {
        var deviceId = "stale-cursor-guard-" + Guid.NewGuid().ToString("N");
        await RegisterReadyDeviceAsync(deviceId);
        for (var index = 0; index < 51; index++)
        {
            await SeedOrderAsync($"RECOVERY-GUARD-{index:D2}", Now.AddHours(-1),
                new RouteSeed(DevicePrintTarget.General, true, DevicePrintStatus.Queued, deviceId));
        }

        var first = await FetchFeedAsync(deviceId, Now.AddMinutes(-5));
        var cursor = first["data"]!["nextOrderCursor"]!.GetValue<string>();
        var fields = DecodeCursorPayload(cursor).Split('|');

        fields[3] = Now.AddHours(-24).ToString("O");
        fields[4] = Now.AddHours(-23).ToString("O");
        var forgedOldFence = EncodeCursorPayload(string.Join('|', fields));
        (await FetchFeedAsync(deviceId, Now.AddMinutes(-5), forgedOldFence))["success"]!
            .GetValue<bool>().Should().BeFalse();

        fields[3] = Now.AddMinutes(-58).ToString("O");
        fields[4] = Now.AddMinutes(2).ToString("O");
        var forgedFutureFence = EncodeCursorPayload(string.Join('|', fields));
        (await FetchFeedAsync(deviceId, Now.AddMinutes(-5), forgedFutureFence))["success"]!
            .GetValue<bool>().Should().BeFalse();

        TestTimeProvider.Advance(TimeSpan.FromMinutes(16));
        (await FetchFeedAsync(deviceId, Now.AddMinutes(-5), cursor))["success"]!
            .GetValue<bool>().Should().BeFalse();
    }

    [Fact]
    public async Task Legacy_date_id_cursor_continues_with_a_fresh_bounded_recovery_window()
    {
        var deviceId = "stale-legacy-cursor-" + Guid.NewGuid().ToString("N");
        await RegisterReadyDeviceAsync(deviceId);
        var expected = await SeedOrderAsync("REC-LEG-CURSOR", Now.AddMinutes(-30),
            new RouteSeed(DevicePrintTarget.General, true, DevicePrintStatus.Queued, deviceId));
        var legacyCursor = RestaurantSystem.Api.Features.Orders.Queries.PrinterFeedUpdatesQuery
            .PrinterFeedUpdateCursor.Encode(Now, Guid.Empty);

        var feed = await FetchFeedAsync(deviceId, Now.AddMinutes(-5), legacyCursor);

        feed["success"]!.GetValue<bool>().Should().BeTrue();
        OrderNumbers(feed).Should().Contain(expected.OrderNumber);
        DecodeCursorPayload(feed["data"]!["nextOrderCursor"]!.GetValue<string>())
            .Should().StartWith("v2|");
    }

    private static string DecodeCursorPayload(string cursor)
    {
        var padded = cursor.Replace('-', '+').Replace('_', '/')
            .PadRight(cursor.Length + ((4 - cursor.Length % 4) % 4), '=');
        return System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(padded));
    }

    private static string EncodeCursorPayload(string payload) => Convert.ToBase64String(
            System.Text.Encoding.UTF8.GetBytes(payload))
        .TrimEnd('=')
        .Replace('+', '-')
        .Replace('/', '_');
}

[Collection("Database Lane 3")]
public sealed class PrinterFeedQueuedRecoveryExtendedWindowTests : PrinterFeedRecoveryTestBase
{
    public PrinterFeedQueuedRecoveryExtendedWindowTests(DatabaseFixture databaseFixture) : base(databaseFixture) { }

    protected override int RecoveryWindowHours => 24;

    [Fact]
    public async Task Explicit_extended_window_covers_three_hours_but_not_older_history()
    {
        var deviceId = "stale-queue-extended-" + Guid.NewGuid().ToString("N");
        await RegisterReadyDeviceAsync(deviceId);
        var recent = await SeedOrderAsync("REC-THREE-HOURS", Now.AddHours(-3),
            new RouteSeed(DevicePrintTarget.General, true, DevicePrintStatus.Queued, deviceId));
        var old = await SeedOrderAsync("REC-OLDER-THAN-DAY", Now.AddHours(-25),
            new RouteSeed(DevicePrintTarget.General, true, DevicePrintStatus.Queued, deviceId));

        var feed = await FetchFeedAsync(deviceId, Now.AddMinutes(-5));

        OrderNumbers(feed).Should().Contain(recent.OrderNumber);
        OrderNumbers(feed).Should().NotContain(old.OrderNumber);
    }
}
