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
public sealed class BillDiscountWorkflowTests(DatabaseFixture fixture) : IntegrationTestBase(fixture)
{
    private readonly Guid _id = Guid.NewGuid();
    private string Route => $"/api/bill-charges/orders/{_id}";
    protected override async Task SeedTestData()
    {
        await base.SeedTestData();
        await using var context = DatabaseFixture.CreateContext();
        var info = await context.RestaurantInfo.FirstAsync();
        info.Currency = "EUR";
        info.BillChargeRulesJson = JsonSerializer.Serialize(new[] { Rule() }, BillChargeRules.JsonOptions);
        context.Orders.Add(new Order
        {
            Id = _id,
            OrderNumber = "DISCOUNT-TEST",
            Type = OrderType.Takeaway,
            Status = OrderStatus.Confirmed,
            Total = 25m,
            SubTotal = 20m,
            Tip = 2m,
            DeliveryFee = 3m,
            RemainingAmount = 25m,
            OrderDate = DateTime.UtcNow,
            PaymentStatus = PaymentStatus.Pending,
            CreatedBy = "test",
            Items = [new OrderItem { Id = Guid.NewGuid(), OrderId = _id, Quantity = 2, ProductName = "Test item", ItemTotal = 20m, CreatedBy = "test" }]
        });
        await context.SaveChangesAsync();
    }

    [Theory]
    [InlineData("Percentage", 10, 23)]
    [InlineData("Fixed", 4, 21)]
    public async Task Staff_discount_keeps_fees_and_tips_and_replays_original_value_after_configuration_edit(
        string calculation, decimal input, decimal expected)
    {
        AuthenticateAsAdmin();
        var config = new BillChargeConfigurationDto("EUR", [Rule() with { Calculation = calculation, DefaultAmount = input }]);
        (await Client.PutAsJsonAsync("/api/bill-charges", config, JsonOptions)).EnsureSuccessStatusCode();
        AuthenticateAsRole(UserRole.Cashier);
        var request = new AddBillChargesRequest(Guid.NewGuid(), await Version(), [new("discount", input, "Reduce", calculation)]);
        var response = await Client.PostAsJsonAsync(Route, request, JsonOptions);
        response.EnsureSuccessStatusCode();
        var dto = (await response.Content.ReadFromJsonAsync<ApiResponse<OrderDto>>(JsonOptions))!.Data!;
        dto.Total.Should().Be(25m);
        dto.PayableTotal.Should().Be(expected);
        dto.RemainingAmount.Should().Be(expected);
        dto.BillCharges.Single().Amount.Should().Be(expected - 25m);
        AuthenticateAsAdmin();
        (await Client.PutAsJsonAsync("/api/bill-charges", config with
        {
            Rules = [Rule() with { Enabled = false, DefaultAmount = 50m }]
        }, JsonOptions)).EnsureSuccessStatusCode();
        var replay = await Client.PostAsJsonAsync(Route, request, JsonOptions);
        replay.EnsureSuccessStatusCode();
        (await replay.Content.ReadFromJsonAsync<ApiResponse<OrderDto>>(JsonOptions))!.Data!.PayableTotal.Should().Be(expected);
        (await Client.PostAsJsonAsync(Route, request with { Charges = [new("discount", input + 1, "Reduce", calculation)] }, JsonOptions))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Percentage_fields_submitted_together_use_the_same_retained_item_basis()
    {
        AuthenticateAsAdmin();
        var config = new BillChargeConfigurationDto("EUR", [Rule(), Rule() with { Key = "extra_discount" }]);
        (await Client.PutAsJsonAsync("/api/bill-charges", config, JsonOptions)).EnsureSuccessStatusCode();
        var response = await Client.PostAsJsonAsync(Route, new AddBillChargesRequest(Guid.NewGuid(), await Version(),
            [new("discount", 10m, "Reduce", "Percentage"), new("extra_discount", 10m, "Reduce", "Percentage")]), JsonOptions);
        response.EnsureSuccessStatusCode();
        var order = (await response.Content.ReadFromJsonAsync<ApiResponse<OrderDto>>(JsonOptions))!.Data!;
        order.PayableTotal.Should().Be(21m); // Two EUR 2 reductions of EUR 20 items; EUR 5 fees/tip retained.
        order.BillCharges.Select(value => value.Amount).Should().Equal(-2m, -2m);
    }

    [Theory]
    [InlineData(-1, HttpStatusCode.BadRequest)]
    [InlineData(101, HttpStatusCode.BadRequest)]
    [InlineData(.001, HttpStatusCode.BadRequest)]
    public async Task Invalid_percentage_inputs_leave_the_order_unchanged(decimal amount, HttpStatusCode expected)
    {
        AuthenticateAsAdmin();
        var response = await Client.PostAsJsonAsync(Route,
            new AddBillChargesRequest(Guid.NewGuid(), await Version(), [new("discount", amount, "Reduce", "Percentage")]), JsonOptions);
        response.StatusCode.Should().Be(expected);
        await using var context = DatabaseFixture.CreateContext();
        (await context.Orders.SingleAsync(value => value.Id == _id)).BillChargeAmount.Should().Be(0m);
    }

    [Fact]
    public async Task Captured_bill_cannot_be_reduced_through_a_discretionary_discount()
    {
        await using (var context = DatabaseFixture.CreateContext())
        {
            var order = await context.Orders.Include(value => value.Payments).SingleAsync(value => value.Id == _id);
            order.TotalPaid = 25m;
            order.RemainingAmount = 0m;
            order.PaymentStatus = PaymentStatus.Completed;
            context.OrderPayments.Add(new OrderPayment
            {
                Id = Guid.NewGuid(),
                OrderId = _id,
                Amount = 25m,
                Currency = "EUR",
                Status = PaymentStatus.Completed,
                PaymentMethod = PaymentMethod.Cash,
                CreatedBy = "test"
            });
            await context.SaveChangesAsync();
        }
        AuthenticateAsAdmin();
        var response = await Client.PostAsJsonAsync(Route,
            new AddBillChargesRequest(Guid.NewGuid(), await Version(), [new("discount", 10m, "Reduce", "Percentage")]), JsonOptions);
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Changed_rule_semantics_and_frozen_tax_are_explicitly_refused()
    {
        AuthenticateAsAdmin();
        var request = new AddBillChargesRequest(Guid.NewGuid(), await Version(), [new("discount", 10m, "Reduce", "Fixed")]);
        (await Client.PostAsJsonAsync(Route, request, JsonOptions)).StatusCode.Should().Be(HttpStatusCode.Conflict);
        await using (var context = DatabaseFixture.CreateContext())
        {
            var order = await context.Orders.SingleAsync(value => value.Id == _id);
            order.Tax = 3m;
            await context.SaveChangesAsync();
        }
        var taxed = request with { ExpectedVersion = await Version(), Charges = [new("discount", 10m, "Reduce", "Percentage")] };
        (await Client.PostAsJsonAsync(Route, taxed, JsonOptions)).StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Discount_is_visible_in_the_end_of_day_report_total_and_category()
    {
        AuthenticateAsAdmin();
        (await Client.PostAsJsonAsync(Route, new AddBillChargesRequest(Guid.NewGuid(), await Version(),
            [new("discount", 10m, "Reduce", "Percentage")]), JsonOptions)).EnsureSuccessStatusCode();
        var reportResponse = await Client.GetAsync("/api/Orders/z-report");
        reportResponse.EnsureSuccessStatusCode();
        var report = (await reportResponse.Content.ReadFromJsonAsync<ApiResponse<ZReportDto>>(JsonOptions))!.Data!;
        report.GrossSales.Should().Be(20m);
        report.NetSales.Should().Be(21m);
        report.TotalTips.Should().Be(2m);
        report.Discounts.TotalDiscounts.Should().Be(2m);
        report.Discounts.StaffDiscounts.Should().Be(2m);
    }

    private async Task<int> Version()
    {
        await using var context = DatabaseFixture.CreateContext();
        return await context.Orders.Where(value => value.Id == _id).Select(value => value.Version).SingleAsync();
    }
    private static BillChargeRuleDto Rule() => new("discount", "Discount", true, 10m, true,
        ["Takeaway"], ["Cashier", "Server"], "Reduce", "Percentage");
}
