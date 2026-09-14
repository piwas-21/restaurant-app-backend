using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

/// <summary>
/// Covers the server-owned orders read contract used by the cashier queue. These tests use real
/// HTTP and PostgreSQL so query binding, SQL translation, total counts and offset pagination are
/// exercised together.
/// </summary>
[Collection("Database Lane 2")]
public class GetOrdersOperationalScopeTests : IntegrationTestBase
{
    private static readonly DateTime YesterdayUtc = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime TodayUtc = YesterdayUtc.AddDays(1);

    public GetOrdersOperationalScopeTests(DatabaseFixture databaseFixture)
        : base(databaseFixture)
    {
    }

    [Fact]
    public async Task Operational_scope_paginates_more_than_one_page_with_a_stable_tie_breaker()
    {
        var first = await GetOrders("scope=Operational&search=OPS-PAGE&page=1&pageSize=5");
        var second = await GetOrders("scope=Operational&search=OPS-PAGE&page=2&pageSize=5");
        var repeatedFirst = await GetOrders("scope=Operational&search=OPS-PAGE&page=1&pageSize=5");

        first.TotalCount.Should().Be(12);
        first.TotalPages.Should().Be(3);
        first.Items.Should().HaveCount(5);
        second.Items.Should().HaveCount(5);
        first.Items.Select(order => order.Id).Should().NotIntersectWith(second.Items.Select(order => order.Id));
        repeatedFirst.Items.Select(order => order.Id).Should().Equal(first.Items.Select(order => order.Id));
    }

    [Fact]
    public async Task Search_is_server_side_and_finds_a_match_on_a_later_page()
    {
        var page = await GetOrders("scope=Operational&search=OPS-LATER&page=2&pageSize=5");

        page.TotalCount.Should().Be(7);
        page.Items.Select(order => order.OrderNumber).Should().Contain("OPS-LATER-01");
    }

    [Fact]
    public async Task Operational_scope_keeps_yesterday_open_work_even_when_date_bounds_exclude_it()
    {
        var page = await GetOrders(
            "scope=Operational&search=OPS-OLD&startDate=2026-01-02T00%3A00%3A00Z&endDate=2026-01-02T23%3A59%3A59Z&pageSize=10");

        page.Items.Select(order => order.OrderNumber).Should().ContainSingle("OPS-OLD-OPEN");
    }

    [Fact]
    public async Task Table_number_filter_is_exact_and_table_number_is_searchable()
    {
        var filtered = await GetOrders("scope=Operational&search=OPS-TABLE&tableNumber=12&pageSize=10");
        var searched = await GetOrders("scope=Operational&search=120&pageSize=10");

        filtered.Items.Select(order => order.OrderNumber).Should().Equal("OPS-TABLE-12");
        searched.Items.Select(order => order.OrderNumber).Should().ContainSingle("OPS-TABLE-120");
    }

    [Fact]
    public async Task Operational_scope_includes_completed_unpaid_but_excludes_refunded_and_cancelled()
    {
        var page = await GetOrders("scope=Operational&search=OPS-SETTLE&pageSize=10");
        var numbers = page.Items.Select(order => order.OrderNumber).ToList();

        numbers.Should().Contain("OPS-SETTLE-UNPAID");
        numbers.Should().NotContain("OPS-SETTLE-REFUND");
        numbers.Should().NotContain("OPS-SETTLE-CANCEL");
    }

    protected override async Task SeedTestData()
    {
        await base.SeedTestData();

        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var orders = new List<Order>();

        for (var index = 1; index <= 12; index++)
        {
            orders.Add(NewOrder($"OPS-PAGE-{index:00}", TodayUtc, OrderStatus.Confirmed, PaymentStatus.Pending));
        }

        for (var index = 1; index <= 7; index++)
        {
            orders.Add(NewOrder($"OPS-LATER-{index:00}", TodayUtc.AddMinutes(index), OrderStatus.Preparing, PaymentStatus.Pending));
        }

        orders.Add(NewOrder("OPS-OLD-OPEN", YesterdayUtc, OrderStatus.Confirmed, PaymentStatus.Pending));
        orders.Add(NewOrder("OPS-TABLE-12", TodayUtc, OrderStatus.Ready, PaymentStatus.Pending, tableNumber: 12));
        orders.Add(NewOrder("OPS-TABLE-120", TodayUtc, OrderStatus.Ready, PaymentStatus.Pending, tableNumber: 120));
        orders.Add(NewOrder("OPS-TABLE-NONE", TodayUtc, OrderStatus.Ready, PaymentStatus.Pending));

        var completedUnpaid = NewOrder(
            "OPS-SETTLE-UNPAID", TodayUtc, OrderStatus.Completed, PaymentStatus.Pending);
        var refunded = NewOrder(
            "OPS-SETTLE-REFUND", TodayUtc, OrderStatus.Completed, PaymentStatus.Refunded);
        var cancelled = NewOrder(
            "OPS-SETTLE-CANCEL", TodayUtc, OrderStatus.Cancelled, PaymentStatus.Pending);
        orders.AddRange(completedUnpaid, refunded, cancelled);

        context.Orders.AddRange(orders);
        context.OrderPayments.Add(new OrderPayment
        {
            OrderId = refunded.Id,
            PaymentMethod = PaymentMethod.Cash,
            Amount = refunded.Total,
            Status = PaymentStatus.Refunded,
            IsRefunded = true,
            RefundedAmount = refunded.Total,
            RefundDate = TodayUtc,
            PaymentDate = TodayUtc,
            CreatedAt = TodayUtc,
            CreatedBy = nameof(GetOrdersOperationalScopeTests),
        });

        await context.SaveChangesAsync();
    }

    private static Order NewOrder(
        string orderNumber,
        DateTime orderDate,
        OrderStatus status,
        PaymentStatus paymentStatus,
        int? tableNumber = null) => new()
        {
            Id = Guid.NewGuid(),
            OrderNumber = orderNumber,
            Type = tableNumber.HasValue ? OrderType.DineIn : OrderType.Takeaway,
            TableNumber = tableNumber,
            Status = status,
            PaymentStatus = paymentStatus,
            SubTotal = 10m,
            Total = 10m,
            TotalPaid = 0m,
            RemainingAmount = 10m,
            OrderDate = orderDate,
            CreatedAt = orderDate,
            CreatedBy = nameof(GetOrdersOperationalScopeTests),
        };

    private async Task<PagedResult<OrderDto>> GetOrders(string queryString)
    {
        AuthenticateAsAdmin();

        var response = await Client.GetAsync($"/api/orders?{queryString}");
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);

        var envelope = JsonSerializer.Deserialize<ApiResponse<PagedResult<OrderDto>>>(body, JsonOptions);
        envelope.Should().NotBeNull();
        envelope!.Success.Should().BeTrue(body);
        envelope.Data.Should().NotBeNull();
        return envelope.Data!;
    }
}
