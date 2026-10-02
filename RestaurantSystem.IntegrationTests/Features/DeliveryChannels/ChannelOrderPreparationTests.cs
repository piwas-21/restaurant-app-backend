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
        (await PutAsJsonAsync($"/api/Orders/{id}/status", new { newStatus = "Preparing" })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var prepared = await PutAsJsonAsync($"/api/Orders/{id}/status", new { newStatus = "Preparing", expectedVersion = order.Version });
        prepared.StatusCode.Should().Be(HttpStatusCode.OK, await prepared.Content.ReadAsStringAsync());
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
        Human(UserRole.Cashier);
        order = (await GetFromJsonAsync<ApiResponse<OrderDto>>($"/api/Orders/{id}"))!.Data!;
        order.Status.Should().Be(OrderStatus.Preparing.ToString());
        order.PermittedActions!.Single(action => action.Action == "MarkReady").Allowed.Should().BeTrue();
        Human(UserRole.Server);
        var server = (await GetFromJsonAsync<ApiResponse<OrderDto>>($"/api/Orders/{id}"))!.Data!;
        server.PermittedActions!.Single(action => action.Action == "MarkReady").Allowed.Should().BeFalse();
        var refused = await PutAsJsonAsync($"/api/Orders/{id}/status", new { newStatus = "Ready", expectedVersion = order.Version });
        refused.StatusCode.Should().Be(HttpStatusCode.OK);
        (await refused.Content.ReadFromJsonAsync<ApiResponse<OrderDto>>(JsonOptions))!.Success.Should().BeFalse();
        Human(UserRole.KitchenStaff);
        (await PutAsJsonAsync($"/api/Orders/{id}/status", new { newStatus = "Ready", expectedVersion = order.Version })).StatusCode.Should().Be(HttpStatusCode.OK);
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
        (await PutAsJsonAsync($"/api/Orders/{id}/status", new { newStatus = "Preparing", expectedVersion = dto.Version })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        await using var context = DatabaseFixture.CreateContext();
        (await context.Orders.SingleAsync(order => order.Id == id)).Status.Should().Be(OrderStatus.Confirmed);
    }

    [Fact]
    public async Task HeldOrderCannotUsePreparationBypass()
    {
        var (id, request) = await HeldOrder(); Human(UserRole.Admin);
        (await PutAsJsonAsync($"/api/Orders/{id}/status", new { newStatus = "Preparing", expectedVersion = request.ExpectedVersion })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        await using var context = DatabaseFixture.CreateContext();
        (await context.Orders.SingleAsync(order => order.Id == id)).Status.Should().Be(OrderStatus.PendingApproval);
    }
}
