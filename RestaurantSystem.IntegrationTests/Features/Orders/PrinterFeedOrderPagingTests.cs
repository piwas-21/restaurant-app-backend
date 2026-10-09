using System.Net;
using System.Text.Json.Nodes;
using FluentAssertions;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

[Collection("Database Lane 2")]
public sealed class PrinterFeedOrderPagingTests : IntegrationTestBase
{
    public PrinterFeedOrderPagingTests(DatabaseFixture databaseFixture) : base(databaseFixture) { }

    [Theory]
    [InlineData(51)]
    [InlineData(100)]
    public async Task Equal_time_orders_are_drained_without_loss_or_duplicates(int count)
    {
        var orderDate = DateTime.UtcNow.AddMinutes(-1);
        await using var context = DatabaseFixture.CreateContext();
        var orders = Enumerable.Range(0, count).Select(index => NewOrder(index, orderDate)).ToArray();
        context.Orders.AddRange(orders);
        await context.SaveChangesAsync();

        var seen = new List<Guid>();
        string? cursor = null;
        var pageCount = 0;
        do
        {
            var page = await FetchAsync(orderDate.AddSeconds(-1), cursor);
            var data = page["data"]!;
            var items = data["items"]!.AsArray();
            items.Count.Should().BeLessThanOrEqualTo(50);
            seen.AddRange(items.Select(item => item!["id"]!.GetValue<Guid>()));
            pageCount++;
            pageCount.Should().BeLessThanOrEqualTo(3);
            if (!data["hasMoreOrders"]!.GetValue<bool>())
                break;
            var next = data["nextOrderCursor"]!.GetValue<string>();
            next.Should().NotBeNullOrWhiteSpace().And.NotBe(cursor);
            cursor = next;
        } while (true);

        seen.Should().OnlyHaveUniqueItems().And.BeEquivalentTo(orders.Select(order => order.Id));
    }

    [Fact]
    public async Task Invalid_order_cursor_fails_the_feed_without_claiming_a_terminal_page()
    {
        var feed = await FetchAsync(DateTime.UtcNow.AddMinutes(-1), "invalid");
        feed["success"]!.GetValue<bool>().Should().BeFalse();
        feed["data"]!["items"]!.AsArray().Should().BeEmpty();
    }

    private async Task<JsonNode> FetchAsync(DateTime modifiedSince, string? cursor)
    {
        AuthenticateAsDevice();
        var url = "/api/orders/printer-feed?modifiedSince=" + Uri.EscapeDataString(modifiedSince.ToString("O"));
        if (cursor is not null)
            url += "&orderCursor=" + Uri.EscapeDataString(cursor);
        var response = await Client.GetAsync(url);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
    }

    private static Order NewOrder(int index, DateTime orderDate) => new()
    {
        Id = Guid.NewGuid(),
        OrderNumber = "PF-PAGE-" + index,
        Type = OrderType.Takeaway,
        Status = OrderStatus.Confirmed,
        PaymentStatus = PaymentStatus.Pending,
        IsKitchenReleased = true,
        OrderDate = orderDate,
        CreatedAt = orderDate,
        CreatedBy = "test"
    };
}
