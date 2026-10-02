using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.DeliveryChannels.Dtos;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Common.Constants;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.DeliveryChannels;

[Collection("Database Lane 2")]
public sealed class ChannelOrderPreparationTests(DatabaseFixture fixture) : ChannelDecisionTestBase(fixture)
{
    [Fact]
    public async Task OnlyAcceptedOrderCanPrepareWithVersionThenProviderOwnsCompletion()
    {
        var (id, request) = await HeldOrder(); await Queue(id, request); var lease = await Claim();
        (await PostAsJsonAsync(ReportEndpoint(lease.DecisionId), Report(lease))).StatusCode.Should().Be(HttpStatusCode.OK);
        Human(UserRole.Cashier);
        var order = (await GetFromJsonAsync<ApiResponse<OrderDto>>($"/api/Orders/{id}"))!.Data!;
        order.PermittedActions!.Single(action => action.Action == "StartPreparing").Allowed.Should().BeTrue();

        Human(UserRole.Server);
        order = (await GetFromJsonAsync<ApiResponse<OrderDto>>($"/api/Orders/{id}"))!.Data!;
        order.PermittedActions!.Single(action => action.Action == "Accept").Allowed.Should().BeFalse();
        order.PermittedActions!.Single(action => action.Action == "StartPreparing").Allowed.Should().BeTrue();
        order.PermittedActions!.Single(action => action.Action == "CollectPayment").Allowed.Should().BeFalse();
        order.PermittedActions!.Single(action => action.Action == "RefundPayment").Allowed.Should().BeFalse();

        var queue = await ReadOperationalMarketplaceQueueAsync(order.OrderNumber);
        queue.TotalCount.Should().Be(1);
        queue.Page.Should().Be(1);
        var queuedOrder = queue.Items.Should().ContainSingle(item => item.Id == id).Subject;
        queuedOrder.PermittedActions!.Single(action => action.Action == "StartPreparing").Allowed.Should().BeTrue();
        queue.Sync!.Mode.Should().Be("Snapshot");
        queue.Sync.Watermark.Should().NotBeNullOrWhiteSpace();
        var watermark = queue.Sync.Watermark!;

        foreach (var role in new[] { UserRole.Admin, UserRole.KitchenStaff })
        {
            Human(role);
            var roleQueue = await ReadOperationalMarketplaceQueueAsync(order.OrderNumber);
            var roleOrder = roleQueue.Items.Single(item => item.Id == id);
            roleOrder.PermittedActions!.Single(action => action.Action == "StartPreparing").Allowed.Should().BeTrue();
        }
        Human(UserRole.Server);

        (await PutAsJsonAsync($"/api/Orders/{id}/status", new { newStatus = "Preparing" }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var stalePreparing = await PutAsJsonAsync($"/api/Orders/{id}/status", new
        {
            newStatus = "Preparing",
            expectedVersion = order.Version - 1
        });
        stalePreparing.StatusCode.Should().Be(HttpStatusCode.OK);
        (await stalePreparing.Content.ReadFromJsonAsync<ApiResponse<OrderDto>>(JsonOptions))!
            .ErrorCode.Should().Be(ErrorCodes.OrderVersionConflict);

        var prepared = await PutAsJsonAsync($"/api/Orders/{id}/status", new
        {
            newStatus = "Preparing",
            expectedVersion = order.Version
        });
        prepared.StatusCode.Should().Be(HttpStatusCode.OK, await prepared.Content.ReadAsStringAsync());

        var changedQueue = await ReadOperationalMarketplaceQueueAsync(order.OrderNumber, watermark);
        changedQueue.Sync!.Mode.Should().Be("Changes");
        var changedOrders = changedQueue.Items.Where(item => item.Id == id).ToArray();
        changedOrders.Should().NotBeEmpty();
        foreach (var changedOrder in changedOrders)
        {
            changedOrder.Status.Should().Be(OrderStatus.Preparing.ToString());
            changedOrder.PermittedActions!.Single(action => action.Action == "MarkReady").Allowed.Should().BeTrue();
        }

        foreach (var role in new[] { UserRole.Admin, UserRole.KitchenStaff })
        {
            Human(role);
            var roleQueue = await ReadOperationalMarketplaceQueueAsync(order.OrderNumber);
            var roleOrder = roleQueue.Items.Single(item => item.Id == id);
            roleOrder.PermittedActions!.Single(action => action.Action == "MarkReady").Allowed.Should().BeTrue();
        }
        Human(UserRole.Server);

        await Gateway();
        (await PostAsJsonAsync($"{Endpoint}/{id}/observe", new ChannelOrderObservation
        {
            Provider = "uber-eats",
            StoreId = "approved-test-store",
            ExternalOrderId = "provider-order-1",
            CanonicalState = "ACCEPTED",
            CanonicalHash = new string('d', 64),
            ObservedAt = DateTimeOffset.UtcNow,
        })).StatusCode.Should().Be(HttpStatusCode.OK);
        Human(UserRole.Server);
        order = (await GetFromJsonAsync<ApiResponse<OrderDto>>($"/api/Orders/{id}"))!.Data!;
        order.Status.Should().Be(OrderStatus.Preparing.ToString());
        order.PermittedActions!.Single(action => action.Action == "MarkReady").Allowed.Should().BeTrue();
        var staleReady = await PutAsJsonAsync($"/api/Orders/{id}/status", new
        {
            newStatus = "Ready",
            expectedVersion = order.Version - 1
        });
        staleReady.StatusCode.Should().Be(HttpStatusCode.OK);
        (await staleReady.Content.ReadFromJsonAsync<ApiResponse<OrderDto>>(JsonOptions))!
            .ErrorCode.Should().Be(ErrorCodes.OrderVersionConflict);

        var markedReady = await PutAsJsonAsync($"/api/Orders/{id}/status", new
        {
            newStatus = "Ready",
            expectedVersion = order.Version
        });
        markedReady.StatusCode.Should().Be(HttpStatusCode.OK, await markedReady.Content.ReadAsStringAsync());
        await using var context = DatabaseFixture.CreateContext();
        var ready = await context.Orders.SingleAsync(order => order.Id == id);
        ready.Status.Should().Be(OrderStatus.Ready); ready.EstimatedDeliveryTime.Should().BeNull();
        (await PutAsJsonAsync($"/api/Orders/{id}/status", new { newStatus = "Completed", expectedVersion = ready.Version })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task OrdinaryMachineWriteScopeCannotPrepareOrAdvertisePreparation()
    {
        var (id, request) = await HeldOrder(); await Queue(id, request); var lease = await Claim();
        (await PostAsJsonAsync(ReportEndpoint(lease.DecisionId), Report(lease))).StatusCode.Should().Be(HttpStatusCode.OK);
        AuthenticateWithToken(await SeedTokenAsync([ApiTokenScopes.OrdersRead, ApiTokenScopes.OrdersWrite]));
        var dto = (await GetFromJsonAsync<ApiResponse<OrderDto>>($"/api/Orders/{id}"))!.Data!;
        dto.PermittedActions!.Single(action => action.Action == "StartPreparing").Allowed.Should().BeFalse();
        dto.PermittedActions!.Single(action => action.Action == "Accept").Allowed.Should().BeFalse();
        dto.PermittedActions!.Single(action => action.Action == "CollectPayment").Allowed.Should().BeFalse();
        dto.PermittedActions!.Single(action => action.Action == "RefundPayment").Allowed.Should().BeFalse();
        var queue = await ReadOperationalMarketplaceQueueAsync(dto.OrderNumber);
        var queuedOrder = queue.Items.Should().ContainSingle(item => item.Id == id).Subject;
        queuedOrder.PermittedActions!.Single(action => action.Action == "StartPreparing").Allowed.Should().BeFalse();
        queuedOrder.PermittedActions!.Single(action => action.Action == "Accept").Allowed.Should().BeFalse();
        queuedOrder.PermittedActions!.Single(action => action.Action == "CollectPayment").Allowed.Should().BeFalse();
        queuedOrder.PermittedActions!.Single(action => action.Action == "RefundPayment").Allowed.Should().BeFalse();
        (await PutAsJsonAsync($"/api/Orders/{id}/status", new { newStatus = "Preparing", expectedVersion = dto.Version })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        await using var context = DatabaseFixture.CreateContext();
        (await context.Orders.SingleAsync(order => order.Id == id)).Status.Should().Be(OrderStatus.Confirmed);
    }

    [Theory]
    [InlineData(UserRole.Admin)]
    [InlineData(UserRole.Server)]
    public async Task HeldPendingOrderCannotUsePreparationBypass(UserRole role)
    {
        var (id, request) = await HeldOrder(); Human(role);
        var order = (await GetFromJsonAsync<ApiResponse<OrderDto>>($"/api/Orders/{id}"))!.Data!;
        order.Status.Should().Be(OrderStatus.PendingApproval.ToString());
        order.PermittedActions!.Single(action => action.Action == "Accept").Allowed.Should().BeFalse();
        order.PermittedActions!.Single(action => action.Action == "StartPreparing").Allowed.Should().BeFalse();
        var queue = await ReadOperationalMarketplaceQueueAsync(order.OrderNumber);
        var queuedOrder = queue.Items.Should().ContainSingle(item => item.Id == id).Subject;
        queuedOrder.PermittedActions!.Single(action => action.Action == "Accept").Allowed.Should().BeFalse();
        queuedOrder.PermittedActions!.Single(action => action.Action == "StartPreparing").Allowed.Should().BeFalse();
        queuedOrder.PermittedActions!.Single(action => action.Action == "MarkReady").Allowed.Should().BeFalse();
        (await PutAsJsonAsync($"/api/Orders/{id}/status", new { newStatus = "Preparing", expectedVersion = request.ExpectedVersion })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        await using var context = DatabaseFixture.CreateContext();
        (await context.Orders.SingleAsync(order => order.Id == id)).Status.Should().Be(OrderStatus.PendingApproval);
    }

    private async Task<PagedResult<OrderDto>> ReadOperationalMarketplaceQueueAsync(
        string orderNumber, string? syncCursor = null)
    {
        var query = $"scope=Operational&marketplaceOnly=true&orderType=Delivery&search={Uri.EscapeDataString(orderNumber)}&page=1&pageSize=10";
        if (syncCursor is not null)
        {
            query += $"&syncCursor={Uri.EscapeDataString(syncCursor)}";
        }

        var response = await Client.GetAsync($"/api/Orders?{query}");
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<ApiResponse<PagedResult<OrderDto>>>(JsonOptions))!.Data!;
    }
}
