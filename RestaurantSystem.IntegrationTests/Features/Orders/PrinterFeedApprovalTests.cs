using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

[Collection("Database Lane 3")]
public sealed class PrinterFeedApprovalTests(DatabaseFixture databaseFixture)
    : PrinterFeedRecoveryTestBase(databaseFixture)
{
    protected override int RecoveryWindowHours => 1;

    [Theory]
    [InlineData(OrderStatus.Pending, false)]
    [InlineData(OrderStatus.PendingApproval, false)]
    [InlineData(OrderStatus.Confirmed, true)]
    [InlineData(OrderStatus.Preparing, true)]
    [InlineData(OrderStatus.Ready, true)]
    [InlineData(OrderStatus.OutForDelivery, true)]
    [InlineData(OrderStatus.Delivered, true)]
    [InlineData(OrderStatus.Completed, true)]
    [InlineData(OrderStatus.Cancelled, false)]
    [InlineData(OrderStatus.Refunded, false)]
    public async Task Queued_cashier_receipt_requires_an_accepted_order(OrderStatus status, bool printable)
    {
        var deviceId = "approval-status-" + Guid.NewGuid().ToString("N");
        await RegisterReadyDeviceAsync(deviceId);
        var order = await SeedOrderAsync("APP-" + status, Now,
            new RouteSeed(DevicePrintTarget.Cashier, false, DevicePrintStatus.Queued, deviceId),
            new RouteSeed(DevicePrintTarget.General, true, DevicePrintStatus.Queued, deviceId));
        await SetOrderStatusAsync(order.Id, status);

        var feed = await FetchFeedAsync(deviceId, Now.AddMinutes(-5));

        if (printable)
            OrderNumbers(feed).Should().ContainSingle(order.OrderNumber);
        else
            OrderNumbers(feed).Should().NotContain(order.OrderNumber,
                "a queued receipt cannot bypass cashier approval or a terminal refusal");

        await using var context = DatabaseFixture.CreateContext();
        var routes = await context.OrderRoutingStates.Where(route => route.OrderId == order.Id).ToListAsync();
        routes.Should().OnlyContain(route => route.Status == DevicePrintStatus.Queued && route.Version == 1,
            "feed eligibility must preserve durable jobs and acknowledgements");
    }

    [Fact]
    public async Task Receipt_is_hidden_until_cashier_saves_preparation_then_contains_the_accepted_time()
    {
        var deviceId = "approval-time-" + Guid.NewGuid().ToString("N");
        await RegisterReadyDeviceAsync(deviceId);
        var order = await SeedOrderAsync("APPROVAL-TIME", Now,
            new RouteSeed(DevicePrintTarget.Cashier, false, DevicePrintStatus.Queued, deviceId));
        await SetOrderStatusAsync(order.Id, OrderStatus.Pending);
        await using (var context = DatabaseFixture.CreateContext())
        {
            // A zero catalogue estimate is valid data, not proof of cashier acceptance.
            await context.Orders.Where(value => value.Id == order.Id)
                .ExecuteUpdateAsync(update => update.SetProperty(value => value.EffectivePreparationMinutes, 0));
        }

        OrderNumbers(await FetchFeedAsync(deviceId, Now.AddMinutes(-5))).Should().NotContain(order.OrderNumber);
        OrderNumbers(await FetchFeedAsync(null, Now.AddMinutes(-5))).Should().NotContain(order.OrderNumber);

        AuthenticateAsAdmin();
        var response = await Client.PostAsJsonAsync($"/api/orders/{order.Id}/approve", new { preparationMinutes = 7 });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var accepted = (await ReadResponseAsync<ApiResponse<OrderDto>>(response))!.Data!;
        accepted.Status.Should().Be(nameof(OrderStatus.Confirmed));
        accepted.EffectivePreparationMinutes.Should().Be(7);
        accepted.ExpectedReadyAt.Should().NotBeNull();

        var feed = await FetchFeedAsync(deviceId, Now.AddMinutes(-5));
        var receipt = Items(feed).Single(value => value!["orderNumber"]!.GetValue<string>() == order.OrderNumber)!;
        receipt["effectivePreparationMinutes"]!.GetValue<int>().Should().Be(7);
        // The feed must contain the persisted timestamp, at PostgreSQL's microsecond precision.
        await using var verify = DatabaseFixture.CreateContext();
        var savedReadyAt = await verify.Orders.AsNoTracking().Where(value => value.Id == order.Id)
            .Select(value => value.ExpectedReadyAt).SingleAsync();
        savedReadyAt.Should().NotBeNull();
        receipt["expectedReadyAt"]!.GetValue<DateTime>().Should().Be(savedReadyAt!.Value);
        Route(feed, order.OrderNumber, DevicePrintTarget.Cashier)["jobId"]!.GetValue<Guid>()
            .Should().Be(order.Routes[0].JobId);
    }
}
