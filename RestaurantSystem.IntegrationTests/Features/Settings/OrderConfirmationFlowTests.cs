using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Features.Settings.Dtos;
using RestaurantSystem.Domain.Common.Constants;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Settings;

/// <summary>
/// The guest-status poll and the confirmation-flow setting behind it (order confirmation flows):
/// the checkout confirmation page polls id + GuestStatusToken anonymously, and the per-type flow a
/// tenant picks in settings drives both that screen and the received-mail copy.
/// </summary>
[Collection("Database Lane 4")]
public sealed class OrderConfirmationFlowTests : IntegrationTestBase
{
    private Guid _productId;

    public OrderConfirmationFlowTests(DatabaseFixture databaseFixture) : base(databaseFixture)
    {
    }

    protected override async Task SeedTestData()
    {
        await base.SeedTestData();
        await using var context = DatabaseFixture.CreateContext();
        _productId = await context.Products
            .Where(product => product.Name == "Test Pizza")
            .Select(product => product.Id)
            .SingleAsync();
    }

    private async Task<(Guid OrderId, string Token, string OrderNumber)> PlaceTakeawayOrder()
    {
        AuthenticateAsAnonymous();
        var response = await Client.PostAsJsonAsync("/api/Orders", new
        {
            type = "Takeaway",
            items = new[] { new { productId = _productId, quantity = 1, unitPrice = 0.01m } }
        });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = (await ReadResponseAsync<ApiResponse<OrderDto>>(response))!;
        body.Data!.Should().NotBeNull();
        body.Data!.GuestStatusToken.Should().NotBeNullOrEmpty("the creation response hands the guest their poll token");
        return (body.Data!.Id, body.Data!.GuestStatusToken!, body.Data!.OrderNumber);
    }

    private async Task ConfigureFlow(OrderType type, string flow, int window)
    {
        AuthenticateAsAdmin();

        // GET first: the service self-seeds missing rows on read, so the PUT always has its row.
        var all = await Client.GetAsync("/api/ordertypeconfiguration");
        var configs = (await ReadResponseAsync<ApiResponse<List<OrderTypeConfigurationDto>>>(all))!;
        var current = configs.Data!.Single(c => c.OrderType == type);

        var put = await Client.PutAsJsonAsync("/api/ordertypeconfiguration", new
        {
            orderType = type.ToString(),
            isEnabled = current.IsEnabled,
            confirmationFlow = flow,
            reviewWindowMinutes = window
        });
        put.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Guest_poll_with_the_right_token_returns_lifecycle_state()
    {
        var (orderId, token, orderNumber) = await PlaceTakeawayOrder();

        AuthenticateAsAnonymous();
        var response = await Client.GetAsync($"/api/orders/guest-status?orderId={orderId}&token={Uri.EscapeDataString(token)}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = (await ReadResponseAsync<ApiResponse<GuestOrderStatusDto>>(response))!;
        body.Data!.Should().NotBeNull();
        body.Data!.OrderNumber.Should().Be(orderNumber);
        body.Data!.Status.Should().Be(OrderStatus.Pending);
        body.Data!.Type.Should().Be(OrderType.Takeaway);
        body.Data!.ConfirmationFlow.Should().Be(OrderConfirmationFlows.Direct);
        body.Data!.ReviewDeadlineUtc.Should().BeNull();
    }

    [Theory]
    [InlineData(10, OrderStatus.Confirmed)]
    [InlineData(15, OrderStatus.PendingApproval)]
    public async Task Approval_endpoint_owns_the_long_preparation_threshold(int preparationMinutes, OrderStatus expectedStatus)
    {
        var (orderId, _, _) = await PlaceTakeawayOrder();
        AuthenticateAsAdmin();

        var response = await Client.PostAsJsonAsync($"/api/orders/{orderId}/approve", new
        {
            preparationMinutes
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = (await ReadResponseAsync<ApiResponse<OrderDto>>(response))!;
        body.Data!.Status.Should().Be(expectedStatus.ToString());
    }

    [Fact]
    public async Task Confirm_now_uses_the_configured_default_preparation_time()
    {
        var (orderId, _, _) = await PlaceTakeawayOrder();
        AuthenticateAsAdmin();
        var beforeApproval = DateTime.UtcNow;

        var response = await Client.PostAsJsonAsync($"/api/orders/{orderId}/approve", new
        {
            preparationMinutes = 0
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = (await ReadResponseAsync<ApiResponse<OrderDto>>(response))!;
        body.Data!.Status.Should().Be(OrderStatus.Confirmed.ToString());
        body.Data!.EstimatedDeliveryTime.Should().BeOnOrAfter(beforeApproval.AddMinutes(20));
        body.Data!.EstimatedDeliveryTime.Should().BeBefore(DateTime.UtcNow.AddMinutes(20).AddSeconds(10));
    }

    [Fact]
    public async Task Approval_endpoint_rejects_an_omitted_preparation_time()
    {
        var (orderId, _, _) = await PlaceTakeawayOrder();
        AuthenticateAsAdmin();

        var response = await Client.PostAsJsonAsync($"/api/orders/{orderId}/approve", new { });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Acknowledge_flow_approval_accepts_a_long_preparation_time_without_customer_consent()
    {
        await ConfigureFlow(OrderType.Takeaway, OrderConfirmationFlows.Acknowledge, 3);
        var (orderId, token, _) = await PlaceTakeawayOrder();
        AuthenticateAsAdmin();

        var response = await Client.PostAsJsonAsync($"/api/orders/{orderId}/approve", new
        {
            preparationMinutes = 45
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = (await ReadResponseAsync<ApiResponse<OrderDto>>(response))!;
        body.Data!.Status.Should().Be(OrderStatus.Confirmed.ToString(),
            "reviewed orders are decided by the restaurant, not sent back to the guest");

        AuthenticateAsAnonymous();
        var guestResponse = await Client.GetAsync(
            $"/api/orders/guest-status?orderId={orderId}&token={Uri.EscapeDataString(token)}");
        var guest = (await ReadResponseAsync<ApiResponse<GuestOrderStatusDto>>(guestResponse))!.Data!;
        guest.ConfirmationFlow.Should().Be(OrderConfirmationFlows.Acknowledge);
        guest.ReviewWindowMinutes.Should().Be(3);
        guest.ReviewDeadlineUtc.Should().NotBeNull();
        guest.EstimatedDeliveryTime.Should().NotBeNull();
    }

    [Fact]
    public async Task Guest_poll_with_a_wrong_token_is_indistinguishable_from_an_unknown_order()
    {
        var (orderId, _, _) = await PlaceTakeawayOrder();

        AuthenticateAsAnonymous();
        var wrong = await Client.GetAsync($"/api/orders/guest-status?orderId={orderId}&token=wrong-token");
        var missing = await Client.GetAsync($"/api/orders/guest-status?orderId={orderId}");
        var unknown = await Client.GetAsync($"/api/orders/guest-status?orderId={Guid.NewGuid()}&token=whatever");

        wrong.StatusCode.Should().Be(HttpStatusCode.NotFound);
        missing.StatusCode.Should().Be(HttpStatusCode.NotFound);
        unknown.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Admin_can_switch_a_type_to_the_acknowledge_flow_and_the_public_read_sees_it()
    {
        await PlaceTakeawayOrder(); // seeds order-type rows through the read path too
        await ConfigureFlow(OrderType.Takeaway, OrderConfirmationFlows.Acknowledge, 3);

        AuthenticateAsAnonymous();
        var publicRead = await Client.GetAsync("/api/ordertypeconfiguration/public");
        publicRead.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = (await ReadResponseAsync<ApiResponse<List<OrderTypeConfirmationPublicDto>>>(publicRead))!;
        var takeaway = body.Data!.Single(c => c.OrderType == OrderType.Takeaway);
        takeaway.ConfirmationFlow.Should().Be(OrderConfirmationFlows.Acknowledge);
        takeaway.ReviewWindowMinutes.Should().Be(3);
    }

    [Fact]
    public async Task An_unknown_flow_is_refused_and_direct_remains_the_default_for_untouched_types()
    {
        await PlaceTakeawayOrder();
        AuthenticateAsAdmin();

        var bad = await Client.PutAsJsonAsync("/api/ordertypeconfiguration", new
        {
            orderType = OrderType.Takeaway.ToString(),
            isEnabled = true,
            confirmationFlow = "approve-it"
        });
        bad.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var all = await Client.GetAsync("/api/ordertypeconfiguration");
        var configs = (await ReadResponseAsync<ApiResponse<List<OrderTypeConfigurationDto>>>(all))!;
        var delivery = configs.Data!.Single(c => c.OrderType == OrderType.Delivery);
        delivery.ConfirmationFlow.Should().Be(OrderConfirmationFlows.Direct, "existing tenants keep today's behaviour until they opt in");
        delivery.ReviewWindowMinutes.Should().Be(OrderTypeConfiguration.DefaultReviewWindowMinutes);
    }
}
