using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.BillCharges.Dtos;
using RestaurantSystem.Api.Features.BillCharges.Services;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

[Collection("Database Lane 4")]
public sealed class BillChargeWorkflowTests(DatabaseFixture fixture) : IntegrationTestBase(fixture)
{
    private readonly Guid _orderId = Guid.NewGuid();
    protected override async Task SeedTestData()
    {
        await base.SeedTestData();
        await using var context = DatabaseFixture.CreateContext();
        var info = await context.RestaurantInfo.FirstAsync();
        info.Currency = "CHF";
        info.BillChargeRulesJson = "[]";
        context.Orders.Add(new Order
        {
            Id = _orderId,
            OrderNumber = "FEE-TEST",
            Type = OrderType.Takeaway,
            Status = OrderStatus.Confirmed,
            Total = 20m,
            SubTotal = 20m,
            RemainingAmount = 20m,
            OrderDate = DateTime.UtcNow,
            PaymentStatus = PaymentStatus.Pending,
            CreatedBy = nameof(BillChargeWorkflowTests)
        });
        await context.SaveChangesAsync();
    }

    [Fact]
    public async Task Only_admin_can_enable_fields_and_zero_is_omitted_from_the_bill()
    {
        AuthenticateAsRole(UserRole.Server);
        var forbidden = await Client.PutAsJsonAsync("/api/bill-charges", Configuration(), JsonOptions);
        forbidden.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        AuthenticateAsAdmin();
        (await Client.PutAsJsonAsync("/api/bill-charges", Configuration(), JsonOptions)).EnsureSuccessStatusCode();
        AuthenticateAsRole(UserRole.Server);
        var version = await VersionAsync();
        var response = await Client.PostAsJsonAsync($"/api/bill-charges/orders/{_orderId}",
            new AddBillChargesRequest(Guid.NewGuid(), version, [new("service_fee", 5m), new("delivery_fee", 0m)]), JsonOptions);
        response.EnsureSuccessStatusCode();
        var dto = (await response.Content.ReadFromJsonAsync<ApiResponse<OrderDto>>(JsonOptions))!.Data!;
        dto.Total.Should().Be(20m);
        dto.PayableTotal.Should().Be(25m);
        dto.BillCharges.Should().ContainSingle().Which.Amount.Should().Be(5m);
    }

    [Fact]
    public async Task Same_operation_replays_once_and_a_different_payload_is_refused()
    {
        AuthenticateAsAdmin();
        (await Client.PutAsJsonAsync("/api/bill-charges", Configuration(), JsonOptions)).EnsureSuccessStatusCode();
        var request = new AddBillChargesRequest(Guid.NewGuid(), await VersionAsync(), [new("service_fee", 5m)]);
        var route = $"/api/bill-charges/orders/{_orderId}";
        (await Client.PostAsJsonAsync(route, request, JsonOptions)).EnsureSuccessStatusCode();
        (await Client.PostAsJsonAsync(route, request, JsonOptions)).EnsureSuccessStatusCode();
        var changed = await Client.PostAsJsonAsync(route, request with { Charges = [new("service_fee", 6m)] }, JsonOptions);
        changed.StatusCode.Should().Be(HttpStatusCode.Conflict);
        await using var context = DatabaseFixture.CreateContext();
        var order = await context.Orders.SingleAsync(o => o.Id == _orderId);
        order.BillChargeAmount.Should().Be(5m);
        OrderBillChargeReader.Read(order).Should().ContainSingle();
    }

    [Fact]
    public async Task Disabled_role_channel_and_non_minor_amounts_are_rejected()
    {
        AuthenticateAsAdmin();
        var request = new AddBillChargesRequest(Guid.NewGuid(), await VersionAsync(), [new("service_fee", 5m)]);
        var route = $"/api/bill-charges/orders/{_orderId}";
        (await Client.PostAsJsonAsync(route, request, JsonOptions)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var config = Configuration();
        config = config with { Rules = [config.Rules[0] with { Roles = ["Cashier"], OrderTypes = ["DineIn"] }] };
        (await Client.PutAsJsonAsync("/api/bill-charges", config, JsonOptions)).EnsureSuccessStatusCode();
        AuthenticateAsRole(UserRole.Server);
        (await Client.PostAsJsonAsync(route, request, JsonOptions)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        AuthenticateAsAdmin();
        (await Client.PutAsJsonAsync("/api/bill-charges", Configuration(), JsonOptions)).EnsureSuccessStatusCode();
        var fractional = request with { Charges = [new("service_fee", 0.001m)] };
        (await Client.PostAsJsonAsync(route, fractional, JsonOptions)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        await using var context = DatabaseFixture.CreateContext();
        (await context.Orders.SingleAsync(o => o.Id == _orderId)).BillChargeAmount.Should().Be(0m);
    }

    private async Task<int> VersionAsync()
    {
        await using var context = DatabaseFixture.CreateContext();
        return await context.Orders.Where(o => o.Id == _orderId).Select(o => o.Version).SingleAsync();
    }

    private static BillChargeConfigurationDto Configuration() => new("CHF",
    [
        new("service_fee", "Service fee", true, 5m, true, ["Takeaway"], ["Server", "Cashier"]),
        new("delivery_fee", "Delivery fee", true, 3m, false, ["Takeaway"], ["Server", "Cashier"])
    ]);
}
