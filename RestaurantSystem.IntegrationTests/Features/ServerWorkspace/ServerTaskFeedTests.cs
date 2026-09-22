using System.Net;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Modules;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Api.Features.ServerWorkspace.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.IntegrationTests.Common;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.ServerWorkspace;

[Collection("Database Lane 2")]
public sealed class ServerTaskFeedTests : IntegrationTestBase
{
    private static readonly DateTime ServerNow = new(2026, 9, 22, 12, 0, 0, DateTimeKind.Utc);
    private Guid _readyDineInId;
    private Guid _readyTakeawayId;
    private Guid _overdueId;
    private Guid _exceptionId;
    private Guid _tableId;
    private Guid _sessionId;

    public ServerTaskFeedTests(DatabaseFixture databaseFixture) : base(databaseFixture)
    {
    }

    protected override void ConfigureTestServices(IServiceCollection services)
    {
        services.RemoveAll<TimeProvider>();
        services.AddSingleton<TimeProvider>(new FixedTimeProvider(ServerNow));
        services.RemoveAll<ITenantModules>();
        services.AddSingleton<ITenantModules>(new TestTenantModules("core,server"));
    }

    protected override async Task SeedTestData()
    {
        await base.SeedTestData();
        await using var context = DatabaseFixture.CreateContext();
        _tableId = Guid.NewGuid();
        _sessionId = Guid.NewGuid();
        context.Tables.Add(new Table
        {
            Id = _tableId,
            TableNumber = "T-7",
            MaxGuests = 4,
            CreatedBy = nameof(ServerTaskFeedTests),
        });
        context.TableServiceSessions.Add(new TableServiceSession
        {
            Id = _sessionId,
            TableId = _tableId,
            Status = TableServiceSessionStatus.Open,
            OpenedAt = ServerNow.AddHours(-1),
            CreatedBy = nameof(ServerTaskFeedTests),
        });

        _readyDineInId = Guid.NewGuid();
        _readyTakeawayId = Guid.NewGuid();
        _overdueId = Guid.NewGuid();
        _exceptionId = Guid.NewGuid();
        var readyDineIn = NewOrder(_readyDineInId, "TASK-READY-DINE", OrderType.DineIn,
            OrderStatus.Ready, ServerNow.AddMinutes(-18), _tableId, _sessionId);
        readyDineIn.StatusHistory.Add(NewHistory(readyDineIn, OrderStatus.Confirmed,
            OrderStatus.Ready, ServerNow.AddMinutes(-12)));
        AddRoute(readyDineIn, DevicePrintTarget.General, DevicePrintStatus.Queued, true);

        var readyTakeaway = NewOrder(_readyTakeawayId, "TASK-READY-TAKE", OrderType.Takeaway,
            OrderStatus.Ready, ServerNow.AddMinutes(-10));
        readyTakeaway.StatusHistory.Add(NewHistory(readyTakeaway, OrderStatus.Confirmed,
            OrderStatus.Ready, ServerNow.AddMinutes(-5)));
        AddRoute(readyTakeaway, DevicePrintTarget.General, DevicePrintStatus.Printed, true);
        AddRoute(readyTakeaway, DevicePrintTarget.Cashier, DevicePrintStatus.Skipped, false);

        var overdue = NewOrder(_overdueId, "TASK-OVERDUE", OrderType.DineIn,
            OrderStatus.Preparing, ServerNow.AddMinutes(-30), _tableId, _sessionId);
        overdue.EstimatedDeliveryTime = ServerNow.AddMinutes(-6);
        AddRoute(overdue, DevicePrintTarget.General, DevicePrintStatus.Queued, true);

        var exception = NewOrder(_exceptionId, "TASK-EXCEPTION", OrderType.Takeaway,
            OrderStatus.Confirmed, ServerNow.AddMinutes(-40));
        exception.EstimatedDeliveryTime = ServerNow.AddMinutes(12);
        AddRoute(exception, DevicePrintTarget.General, DevicePrintStatus.Failed, true);
        AddRoute(exception, DevicePrintTarget.Cashier, DevicePrintStatus.NotConfigured, false);

        context.Orders.AddRange(readyDineIn, readyTakeaway, overdue, exception);
        await context.SaveChangesAsync();
    }

    [Fact]
    public async Task Feed_sorts_buckets_uses_server_time_and_keeps_takeaway_unlinked()
    {
        AuthenticateAsRole(UserRole.Server);

        var response = await Client.GetAsync("/api/staff/server-workspace/tasks?pageSize=10");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = (await ReadResponseAsync<ApiResponse<ServerTaskFeedDto>>(response))!;
        var feed = body.Data!;

        feed.ServerTime.Should().Be(ServerNow);
        feed.TotalCount.Should().Be(4);
        feed.Items.Select(item => item.OrderId).Should().Equal(
            _readyDineInId, _readyTakeawayId, _overdueId, _exceptionId);
        feed.Items.Select(item => item.Bucket).Should().Equal(
            "Ready", "Ready", "Overdue", "Exception");
        feed.Items[0].AgeSeconds.Should().Be(720);
        feed.Items[0].TableId.Should().Be(_tableId);
        feed.Items[0].ServiceSessionId.Should().Be(_sessionId);
        feed.Items[1].TableId.Should().BeNull();
        feed.Items[1].TableLabel.Should().BeNull();
        feed.Items[1].TableNumber.Should().BeNull();
        feed.Items[1].ServiceSessionId.Should().BeNull();
        feed.Items[1].Bucket.Should().Be("Ready");
        feed.Items[1].HasOptionalRoutingException.Should().BeTrue();
        feed.Items[1].HasRequiredRoutingException.Should().BeFalse();
        feed.Items[1].RoutingState.Should().Be("ExceptionOptional");
        feed.Items[2].PermittedDeliveryActions.Single().Allowed.Should().BeFalse();
        feed.Items[2].PermittedDeliveryActions.Single().ReasonCode
            .Should().Be(OrderActionReasonCodes.InvalidStatusTransition);

        await using (var context = DatabaseFixture.CreateContext())
        {
            await context.Orders
                .Where(order => order.Id == _readyTakeawayId)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(order => order.IsKitchenReleased, false)
                    .SetProperty(order => order.Version, order => order.Version + 1));
        }

        var heldFeed = (await ReadResponseAsync<ApiResponse<ServerTaskFeedDto>>(
            await Client.GetAsync("/api/staff/server-workspace/tasks?bucket=ready")))!.Data!;
        heldFeed.Items.Single(item => item.OrderId == _readyTakeawayId)
            .PermittedDeliveryActions.Single().ReasonCode
            .Should().Be(ErrorCodes.KitchenReleaseRequired);
    }

    [Fact]
    public async Task Tasks_require_authenticated_table_service_staff()
    {
        AuthenticateAsAnonymous();
        (await Client.GetAsync("/api/staff/server-workspace/tasks")).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized);

        AuthenticateAsRole(UserRole.KitchenStaff);
        (await Client.GetAsync("/api/staff/server-workspace/tasks")).StatusCode
            .Should().Be(HttpStatusCode.Forbidden);

        AuthenticateAsRole(UserRole.Server);
        (await Client.GetAsync("/api/staff/server-workspace/tasks")).StatusCode
            .Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Cursor_is_bound_to_its_bucket_filter()
    {
        AuthenticateAsRole(UserRole.Server);
        var snapshot = (await ReadResponseAsync<ApiResponse<ServerTaskFeedDto>>(
            await Client.GetAsync("/api/staff/server-workspace/tasks?pageSize=1")))!.Data!;

        var response = await Client.GetAsync(
            $"/api/staff/server-workspace/tasks?bucket=ready&cursor={Uri.EscapeDataString(snapshot.NextCursor!)}");
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = (await ReadResponseAsync<ApiResponse<ServerTaskFeedDto>>(response))!;
        body.ErrorCode.Should().Be(ErrorCodes.InvalidOperationalQueueCursor);
    }

    [Fact]
    public async Task Routing_exception_exposes_required_and_optional_truth()
    {
        AuthenticateAsRole(UserRole.Server);

        var feed = (await ReadResponseAsync<ApiResponse<ServerTaskFeedDto>>(
            await Client.GetAsync("/api/staff/server-workspace/tasks?bucket=exception")))!.Data!;
        var task = feed.Items.Single();

        task.HasRequiredRoutingException.Should().BeTrue();
        task.HasOptionalRoutingException.Should().BeTrue();
        task.RoutingState.Should().Be("ExceptionRequired");
        task.PermittedDeliveryActions.Single().Allowed.Should().BeFalse();
        task.PermittedDeliveryActions.Single().ReasonCode
            .Should().Be(ErrorCodes.RequiredRoutingUnresolved);
        task.Routing.Should().Contain(state =>
            state.Target == DevicePrintTarget.General
            && state.IsRequired
            && state.Status == DevicePrintStatus.Failed);
        task.Routing.Should().Contain(state =>
            state.Target == DevicePrintTarget.Cashier
            && !state.IsRequired
            && state.Status == DevicePrintStatus.NotConfigured);

        var refusal = await PostAsJsonAsync(
            $"/api/staff/server-workspace/tasks/{task.OrderId}/deliver",
            new { expectedVersion = task.Version });
        var refusalBody = (await ReadResponseAsync<ApiResponse<OrderDto>>(refusal))!;
        refusalBody.Success.Should().BeFalse();
        refusalBody.ErrorCode.Should().Be(ErrorCodes.RequiredRoutingUnresolved);
    }

    [Fact]
    public async Task Snapshot_cursor_pages_without_duplicates_then_change_feed_upserts_and_removes()
    {
        AuthenticateAsRole(UserRole.Server);
        var first = (await ReadResponseAsync<ApiResponse<ServerTaskFeedDto>>(
            await Client.GetAsync("/api/staff/server-workspace/tasks?pageSize=2")))!.Data!;
        first.Items.Should().HaveCount(2);
        first.HasMore.Should().BeTrue();
        first.NextCursor.Should().NotBeNullOrWhiteSpace();

        var second = (await ReadResponseAsync<ApiResponse<ServerTaskFeedDto>>(
            await Client.GetAsync($"/api/staff/server-workspace/tasks?pageSize=2&cursor={Uri.EscapeDataString(first.NextCursor!)}")))!.Data!;
        second.Items.Select(item => item.OrderId).Should().Equal(_overdueId, _exceptionId);
        second.TotalCount.Should().Be(4);
        second.HasMore.Should().BeFalse();
        second.NextCursor.Should().NotBeNullOrWhiteSpace();

        await using (var context = DatabaseFixture.CreateContext())
        {
            await context.Orders
                .Where(order => order.Id == _overdueId)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(order => order.Status, OrderStatus.Ready)
                    .SetProperty(order => order.Version, order => order.Version + 1));
        }

        await using (var context = DatabaseFixture.CreateContext())
        {
            await context.Orders
                .Where(order => order.Id == _readyTakeawayId)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(order => order.Status, OrderStatus.OutForDelivery)
                    .SetProperty(order => order.Version, order => order.Version + 1));
        }

        await using (var context = DatabaseFixture.CreateContext())
        {
            await context.Orders
                .Where(order => order.Id == _exceptionId)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(order => order.Status, OrderStatus.Completed)
                    .SetProperty(order => order.Version, order => order.Version + 1));
        }

        await using (var context = DatabaseFixture.CreateContext())
        {
            await context.Orders
                .Where(order => order.Id == _readyDineInId)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(order => order.Status, OrderStatus.Completed)
                    .SetProperty(order => order.Version, order => order.Version + 1));
        }

        var changed = (await ReadResponseAsync<ApiResponse<ServerTaskFeedDto>>(
            await Client.GetAsync($"/api/staff/server-workspace/tasks?pageSize=2&cursor={Uri.EscapeDataString(second.NextCursor!)}")))!.Data!;
        changed.Items.Select(item => item.OrderId).Should().Equal(_overdueId, _readyTakeawayId);
        changed.Items.Select(item => item.Bucket).Should().Equal("Ready", "Ready");
        changed.HasMore.Should().BeTrue();

        var removed = (await ReadResponseAsync<ApiResponse<ServerTaskFeedDto>>(
            await Client.GetAsync($"/api/staff/server-workspace/tasks?pageSize=2&cursor={Uri.EscapeDataString(changed.NextCursor!)}")))!.Data!;
        removed.RemovedOrderIds.Should().Equal(_exceptionId, _readyDineInId);
        removed.Items.Should().BeEmpty();
        removed.HasMore.Should().BeFalse();
        removed.TotalCount.Should().Be(2);
    }

    [Fact]
    public async Task Delivery_requires_current_version_and_never_performs_kitchen_transition()
    {
        AuthenticateAsRole(UserRole.Server);
        var feed = (await ReadResponseAsync<ApiResponse<ServerTaskFeedDto>>(
            await Client.GetAsync("/api/staff/server-workspace/tasks?bucket=ready")))!.Data!;
        var ready = feed.Items.Single(item => item.OrderId == _readyDineInId);
        var optionalRoute = feed.Items.Single(item => item.OrderId == _readyTakeawayId);
        optionalRoute.HasOptionalRoutingException.Should().BeTrue();
        optionalRoute.PermittedDeliveryActions.Single().Allowed.Should().BeTrue();

        var stale = await PostAsJsonAsync(
            $"/api/staff/server-workspace/tasks/{ready.OrderId}/deliver",
            new { expectedVersion = ready.Version + 1 });
        var staleBody = (await ReadResponseAsync<ApiResponse<OrderDto>>(stale))!;
        staleBody.Success.Should().BeFalse();
        staleBody.ErrorCode.Should().Be(ErrorCodes.OrderVersionConflict);

        var overdue = (await ReadResponseAsync<ApiResponse<ServerTaskFeedDto>>(
            await Client.GetAsync("/api/staff/server-workspace/tasks?bucket=overdue")))!.Data!.Items.Single();
        var kitchenAttempt = await PostAsJsonAsync(
            $"/api/staff/server-workspace/tasks/{overdue.OrderId}/deliver",
            new { expectedVersion = overdue.Version });
        var kitchenBody = (await ReadResponseAsync<ApiResponse<OrderDto>>(kitchenAttempt))!;
        kitchenBody.Success.Should().BeFalse();
        kitchenBody.ErrorCode.Should().Be(OrderActionReasonCodes.InvalidStatusTransition);

        var optionalDelivery = await PostAsJsonAsync(
            $"/api/staff/server-workspace/tasks/{optionalRoute.OrderId}/deliver",
            new { expectedVersion = optionalRoute.Version });
        var optionalDeliveryBody = (await ReadResponseAsync<ApiResponse<OrderDto>>(optionalDelivery))!;
        optionalDeliveryBody.Success.Should().BeTrue();

        var delivered = await PostAsJsonAsync(
            $"/api/staff/server-workspace/tasks/{ready.OrderId}/deliver",
            new { expectedVersion = ready.Version });
        var deliveredBody = (await ReadResponseAsync<ApiResponse<OrderDto>>(delivered))!;
        deliveredBody.Success.Should().BeTrue();
        deliveredBody.Data!.Status.Should().Be(nameof(OrderStatus.Completed));

        var retry = await PostAsJsonAsync(
            $"/api/staff/server-workspace/tasks/{ready.OrderId}/deliver",
            new { expectedVersion = ready.Version });
        var retryBody = (await ReadResponseAsync<ApiResponse<OrderDto>>(retry))!;
        retryBody.Success.Should().BeFalse();
        retryBody.ErrorCode.Should().Be(ErrorCodes.OrderVersionConflict);
    }

    [Fact]
    public async Task Required_skipped_route_is_an_exception_but_optional_skipped_route_is_not()
    {
        AuthenticateAsRole(UserRole.Server);

        await using (var context = DatabaseFixture.CreateContext())
        {
            var route = await context.OrderRoutingStates.SingleAsync(state =>
                state.OrderId == _readyDineInId && state.IsRequired);
            route.Status = DevicePrintStatus.Skipped;
            await context.SaveChangesAsync();
        }

        var exceptions = (await ReadResponseAsync<ApiResponse<ServerTaskFeedDto>>(
            await Client.GetAsync("/api/staff/server-workspace/tasks?bucket=exception")))!.Data!;
        var blocked = exceptions.Items.Single(item => item.OrderId == _readyDineInId);
        blocked.HasRequiredRoutingException.Should().BeTrue();
        blocked.Routing.Should().Contain(state => state.Status == DevicePrintStatus.Skipped && state.IsRequired);
        blocked.PermittedDeliveryActions.Single().Allowed.Should().BeFalse();

        var ready = (await ReadResponseAsync<ApiResponse<ServerTaskFeedDto>>(
            await Client.GetAsync("/api/staff/server-workspace/tasks?bucket=ready")))!.Data!;
        var optional = ready.Items.Single(item => item.OrderId == _readyTakeawayId);
        optional.HasOptionalRoutingException.Should().BeTrue();
        optional.HasRequiredRoutingException.Should().BeFalse();
        optional.PermittedDeliveryActions.Single().Allowed.Should().BeTrue();
    }

    [Fact]
    public async Task Tracked_route_mutation_fences_delivery_with_the_parent_order_version()
    {
        AuthenticateAsRole(UserRole.Server);
        var feed = (await ReadResponseAsync<ApiResponse<ServerTaskFeedDto>>(
            await Client.GetAsync("/api/staff/server-workspace/tasks?bucket=ready")))!.Data!;
        var task = feed.Items.Single(item => item.OrderId == _readyDineInId);

        await using (var context = DatabaseFixture.CreateContext())
        {
            var route = await context.OrderRoutingStates.SingleAsync(state =>
                state.OrderId == _readyDineInId && state.IsRequired);
            route.Status = DevicePrintStatus.Sent;
            route.Version++;
            await context.SaveChangesAsync();
        }

        await using (var context = DatabaseFixture.CreateContext())
        {
            var order = await context.Orders.SingleAsync(order => order.Id == _readyDineInId);
            order.Version.Should().BeGreaterThan(task.Version);
        }

        var stale = await PostAsJsonAsync(
            $"/api/staff/server-workspace/tasks/{task.OrderId}/deliver",
            new { expectedVersion = task.Version });
        var body = (await ReadResponseAsync<ApiResponse<OrderDto>>(stale))!;
        body.Success.Should().BeFalse();
        body.ErrorCode.Should().Be(ErrorCodes.OrderVersionConflict);
    }

    [Fact]
    public async Task Page_size_one_walks_many_candidates_with_keyset_pages()
    {
        AuthenticateAsRole(UserRole.Server);
        var extraIds = new List<Guid>();
        await using (var context = DatabaseFixture.CreateContext())
        {
            for (var index = 0; index < 12; index++)
            {
                var id = Guid.NewGuid();
                var order = NewOrder(
                    id,
                    $"TASK-MANY-{index:00}",
                    OrderType.Takeaway,
                    OrderStatus.Ready,
                    ServerNow.AddMinutes(-100 - index));
                AddRoute(order, DevicePrintTarget.General, DevicePrintStatus.Printed, true);
                context.Orders.Add(order);
                extraIds.Add(id);
            }

            await context.SaveChangesAsync();
        }

        var seen = new HashSet<Guid>();
        string? cursor = null;
        for (var page = 0; page < extraIds.Count + 4; page++)
        {
            var suffix = cursor is null
                ? "pageSize=1"
                : $"pageSize=1&cursor={Uri.EscapeDataString(cursor)}";
            var feed = (await ReadResponseAsync<ApiResponse<ServerTaskFeedDto>>(
                await Client.GetAsync($"/api/staff/server-workspace/tasks?{suffix}")))!.Data!;
            feed.Items.Should().HaveCountLessThanOrEqualTo(1);
            foreach (var item in feed.Items)
            {
                seen.Add(item.OrderId).Should().BeTrue("each keyset page must advance past its prior row");
            }

            if (!feed.HasMore)
            {
                break;
            }

            cursor = feed.NextCursor;
            cursor.Should().NotBeNullOrWhiteSpace();
        }

        seen.Should().Contain(extraIds);
        seen.Should().OnlyHaveUniqueItems();
    }

    [Theory]
    [InlineData(UserRole.Server)]
    [InlineData(UserRole.Cashier)]
    [InlineData(UserRole.Admin)]
    public async Task Delivery_endpoint_allows_each_table_service_role(UserRole role)
    {
        if (role == UserRole.Admin)
        {
            AuthenticateAsAdmin();
        }
        else
        {
            AuthenticateAsRole(role);
        }

        var feed = (await ReadResponseAsync<ApiResponse<ServerTaskFeedDto>>(
            await Client.GetAsync("/api/staff/server-workspace/tasks?bucket=ready")))!.Data!;
        var task = feed.Items.Single(item => item.OrderId == _readyTakeawayId);
        var response = await PostAsJsonAsync(
            $"/api/staff/server-workspace/tasks/{task.OrderId}/deliver",
            new { expectedVersion = task.Version });
        var body = (await ReadResponseAsync<ApiResponse<OrderDto>>(response))!;
        body.Success.Should().BeTrue();
    }

    private static Order NewOrder(
        Guid id,
        string number,
        OrderType type,
        OrderStatus status,
        DateTime orderDate,
        Guid? tableId = null,
        Guid? sessionId = null) => new()
        {
            Id = id,
            OrderNumber = number,
            Type = type,
            Status = status,
            PaymentStatus = PaymentStatus.Pending,
            OrderDate = orderDate,
            Total = 25m,
            RemainingAmount = 25m,
            TableId = tableId,
            ServiceSessionId = sessionId,
            TableLabel = tableId.HasValue ? "T-7" : null,
            TableNumber = tableId.HasValue ? 7 : null,
            CreatedBy = nameof(ServerTaskFeedTests),
        };

    private static OrderStatusHistory NewHistory(
        Order order,
        OrderStatus from,
        OrderStatus to,
        DateTime changedAt) => new()
        {
            Id = Guid.NewGuid(),
            OrderId = order.Id,
            FromStatus = from,
            ToStatus = to,
            ChangedAt = changedAt,
            ChangedBy = nameof(ServerTaskFeedTests),
            CreatedBy = nameof(ServerTaskFeedTests),
        };

    private static void AddRoute(
        Order order,
        DevicePrintTarget target,
        DevicePrintStatus status,
        bool required) => order.RoutingStates.Add(new OrderRoutingState
        {
            Id = Guid.NewGuid(),
            OrderId = order.Id,
            JobId = Guid.NewGuid(),
            Target = target,
            Status = status,
            IsRequired = required,
            CreatedAt = order.OrderDate,
            UpdatedAt = order.OrderDate,
            CreatedBy = nameof(ServerTaskFeedTests),
        });

    private sealed class FixedTimeProvider(DateTime utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utcNow, TimeSpan.Zero);
    }
}

[Collection("Database Lane 2")]
public sealed class ServerTaskFeedModuleTests
{
    private readonly DatabaseFixture _fixture;

    public ServerTaskFeedModuleTests(DatabaseFixture fixture) => _fixture = fixture;

    [Theory]
    [InlineData(UserRole.Server, "core,server", HttpStatusCode.OK)]
    [InlineData(UserRole.Cashier, "core,cashier", HttpStatusCode.OK)]
    [InlineData(UserRole.Admin, "core,server", HttpStatusCode.OK)]
    [InlineData(UserRole.KitchenStaff, "core,server", HttpStatusCode.Forbidden)]
    [InlineData(UserRole.Server, "core", HttpStatusCode.NotFound)]
    [InlineData(UserRole.Cashier, "core", HttpStatusCode.NotFound)]
    [InlineData(UserRole.Admin, "core", HttpStatusCode.NotFound)]
    [InlineData(UserRole.Server, "", HttpStatusCode.OK)]
    public async Task Tasks_obey_the_role_and_server_or_cashier_module_matrix(
        UserRole role, string enabledModules, HttpStatusCode expected)
    {
        await _fixture.ResetDatabaseAsync();
        using var factory = new TestWebApplicationFactory(
            _fixture.ConnectionString,
            new Dictionary<string, string>
            {
                ["Modules:Enforce"] = "true",
                ["Modules:Enabled"] = enabledModules
            });
        using (var scope = factory.Services.CreateScope())
        {
            await TestDataSeeder.SeedBasicDataAsync(
                scope.ServiceProvider.GetRequiredService<ApplicationDbContext>());
        }

        using var client = factory.CreateClient();
        if (role == UserRole.Admin)
        {
            client.DefaultRequestHeaders.Add("X-Test-Admin", "true");
        }
        else
        {
            client.DefaultRequestHeaders.Add("X-Test-Role", role.ToString());
        }

        var response = await client.GetAsync("/api/staff/server-workspace/tasks");
        response.StatusCode.Should().Be(expected);
    }
}

internal sealed class TestTenantModules(string enabled) : ITenantModules
{
    private readonly HashSet<string> _enabled = enabled.Split(',').ToHashSet(StringComparer.OrdinalIgnoreCase);

    public bool IsEnforced => true;
    public IReadOnlyList<string> EnabledModules => _enabled.ToList();
    public bool IsEnabled(string moduleId) => _enabled.Contains(moduleId);
}
