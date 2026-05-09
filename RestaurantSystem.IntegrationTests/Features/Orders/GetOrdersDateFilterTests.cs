using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Infrastructure;
using System.Net;
using System.Text.Json;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

/// <summary>
/// Verifies the <c>StartDate</c>/<c>EndDate</c> filter on GET /api/orders.
/// Both bounds are inclusive (<c>OrderDate &gt;= StartDate AND OrderDate &lt;= EndDate</c>),
/// compared verbatim in UTC. See <c>GetOrdersQuery</c> XML doc.
/// </summary>
public class GetOrdersDateFilterTests : IntegrationTestBase
{
    public GetOrdersDateFilterTests(DatabaseFixture databaseFixture)
        : base(databaseFixture)
    {
    }

    /// <summary>
    /// Anchor used for all test orders so we can deterministically assert filter windows
    /// independent of wall-clock time.
    /// </summary>
    private static readonly DateTime AnchorUtc =
        new DateTime(2026, 4, 15, 12, 0, 0, DateTimeKind.Utc);

    protected override async Task SeedTestData()
    {
        await base.SeedTestData();

        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        // Five orders at known UTC instants relative to AnchorUtc.
        // Spread spans 7 days so window queries can pin each unambiguously.
        var orders = new[]
        {
            CreateOrder("ORD-D7", AnchorUtc.AddDays(-7)),
            CreateOrder("ORD-D2", AnchorUtc.AddDays(-2)),
            CreateOrder("ORD-D0", AnchorUtc),               // exact anchor
            CreateOrder("ORD-D2F", AnchorUtc.AddDays(2)),
            CreateOrder("ORD-D7F", AnchorUtc.AddDays(7)),
        };

        context.Orders.AddRange(orders);
        await context.SaveChangesAsync();
    }

    private static Order CreateOrder(string orderNumber, DateTime orderDate) => new()
    {
        Id = Guid.NewGuid(),
        OrderNumber = orderNumber,
        OrderDate = orderDate,
        Type = OrderType.Takeaway,
        Status = OrderStatus.Confirmed,
        PaymentStatus = PaymentStatus.Completed,
        SubTotal = 10m,
        Total = 10m,
        IsDeleted = false,
        CreatedAt = orderDate,
        CreatedBy = "test",
    };

    [Fact]
    public async Task NoDateFilter_ReturnsAllSeededOrders()
    {
        AuthenticateAsAdmin();

        var result = await GetOrders("/api/orders?pageSize=50");

        result.Should().NotBeNull();
        result!.Items.Select(o => o.OrderNumber)
            .Should().Contain(new[] { "ORD-D7", "ORD-D2", "ORD-D0", "ORD-D2F", "ORD-D7F" });
    }

    [Fact]
    public async Task StartDateOnly_FiltersInclusiveLowerBound()
    {
        AuthenticateAsAdmin();

        // startDate = anchor → expect anchor + 2 forward orders
        var startIso = Uri.EscapeDataString(AnchorUtc.ToString("o"));
        var result = await GetOrders($"/api/orders?startDate={startIso}&pageSize=50");

        var numbers = result!.Items.Select(o => o.OrderNumber).ToList();
        numbers.Should().Contain(new[] { "ORD-D0", "ORD-D2F", "ORD-D7F" });
        numbers.Should().NotContain(new[] { "ORD-D7", "ORD-D2" });
    }

    [Fact]
    public async Task EndDateOnly_FiltersInclusiveUpperBound()
    {
        AuthenticateAsAdmin();

        // endDate = anchor → expect anchor + 2 backward orders
        var endIso = Uri.EscapeDataString(AnchorUtc.ToString("o"));
        var result = await GetOrders($"/api/orders?endDate={endIso}&pageSize=50");

        var numbers = result!.Items.Select(o => o.OrderNumber).ToList();
        numbers.Should().Contain(new[] { "ORD-D7", "ORD-D2", "ORD-D0" });
        numbers.Should().NotContain(new[] { "ORD-D2F", "ORD-D7F" });
    }

    [Fact]
    public async Task BothDates_FiltersToWindow()
    {
        AuthenticateAsAdmin();

        // Window: [anchor-3d, anchor+3d] → expect ORD-D2, ORD-D0, ORD-D2F
        var startIso = Uri.EscapeDataString(AnchorUtc.AddDays(-3).ToString("o"));
        var endIso = Uri.EscapeDataString(AnchorUtc.AddDays(3).ToString("o"));
        var result = await GetOrders($"/api/orders?startDate={startIso}&endDate={endIso}&pageSize=50");

        var numbers = result!.Items.Select(o => o.OrderNumber).ToList();
        numbers.Should().BeEquivalentTo(new[] { "ORD-D2", "ORD-D0", "ORD-D2F" });
    }

    [Fact]
    public async Task BothDates_BoundariesAreInclusive()
    {
        AuthenticateAsAdmin();

        // Window: [anchor, anchor] → only ORD-D0 (exactly at boundary on both ends)
        var anchorIso = Uri.EscapeDataString(AnchorUtc.ToString("o"));
        var result = await GetOrders($"/api/orders?startDate={anchorIso}&endDate={anchorIso}&pageSize=50");

        var numbers = result!.Items.Select(o => o.OrderNumber).ToList();
        numbers.Should().BeEquivalentTo(new[] { "ORD-D0" });
    }

    [Fact]
    public async Task StartAfterEnd_ReturnsEmpty()
    {
        AuthenticateAsAdmin();

        // Inverted window — defensive: should not error, just return empty
        var startIso = Uri.EscapeDataString(AnchorUtc.AddDays(1).ToString("o"));
        var endIso = Uri.EscapeDataString(AnchorUtc.AddDays(-1).ToString("o"));
        var result = await GetOrders($"/api/orders?startDate={startIso}&endDate={endIso}&pageSize=50");

        result!.Items.Should().BeEmpty();
        result.TotalCount.Should().Be(0);
    }

    [Fact]
    public async Task InvalidDateFormat_Returns400()
    {
        AuthenticateAsAdmin();

        var response = await Client.GetAsync("/api/orders?startDate=not-a-date");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    /// <summary>
    /// Helper: GET /api/orders with the given query string and deserialize the paged-result envelope.
    /// </summary>
    private async Task<PagedResult<OrderDto>?> GetOrders(string url)
    {
        var response = await Client.GetAsync(url);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadAsStringAsync();
        var envelope = JsonSerializer.Deserialize<ApiResponse<PagedResult<OrderDto>>>(json, JsonOptions);
        envelope.Should().NotBeNull();
        envelope!.Success.Should().BeTrue();
        return envelope.Data;
    }
}
