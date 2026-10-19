using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.IntegrationTests.Common;
using RestaurantSystem.IntegrationTests.Features.Basket;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

[Collection("Database Lane 4")]
public sealed partial class CustomerOrderHistoryTests(DatabaseFixture fixture) : IntegrationTestBase(fixture)
{
    private const string Path = "/api/customer-mobile/orders";
    private static readonly Guid Owner = Guid.Parse(TestAuthHandler.UserId);
    private static readonly Guid Foreign = Guid.Parse(TestAuthHandler.AdminUserId);
    private static readonly DateTime Instant = new(2026, 10, 19, 12, 0, 0, DateTimeKind.Utc);
    protected override void ConfigureTestServices(IServiceCollection services) =>
        services.PostConfigure<AuthenticationOptions>(options =>
        {
            options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
            options.DefaultForbidScheme = JwtBearerDefaults.AuthenticationScheme;
        });

    [Fact]
    public async Task History_pages_are_recent_first_stably_tied_owned_and_private_without_loading_line_graphs()
    {
        AuthenticateCustomer();
        await using (var seed = DatabaseFixture.CreateContext())
        {
            seed.Orders.AddRange(Enumerable.Range(1, 22).Reverse().Select(index => Order(index, Owner)));
            seed.Orders.Add(Order(23, Foreign));
            var deleted = Order(24, Owner);
            deleted.IsDeleted = true;
            seed.Orders.Add(deleted);
            var anonymous = Order(25, null);
            seed.Orders.Add(anonymous);
            await seed.SaveChangesAsync();
        }
        var first = await ReadAsync("?schemaVersion=1&userId=" + Foreign);
        var items = first.GetProperty("items").EnumerateArray().ToArray();
        items.Select(item => item.GetProperty("orderId").GetGuid()).Should().Equal(Enumerable.Range(1, 20).Select(Id));
        items[0].EnumerateObject().Select(item => item.Name).Should().BeEquivalentTo(
            "orderId", "orderNumber", "type", "orderDate", "status", "paymentStatus", "currency", "payableTotal");
        items[0].GetProperty("currency").ValueKind.Should().Be(JsonValueKind.Null);
        items[0].GetProperty("payableTotal").ValueKind.Should().Be(JsonValueKind.Null);
        var cursor = first.GetProperty("nextBefore");
        cursor.GetProperty("orderId").GetGuid().Should().Be(Id(20));
        var second = await ReadAsync(Cursor(cursor));
        second.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("orderId").GetGuid())
            .Should().Equal(Id(21), Id(22));
        second.GetProperty("nextBefore").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task Historical_currency_comes_only_from_immutable_snapshot_and_current_credits_are_separate_from_settlement_status()
    {
        AuthenticateCustomer();
        await using (var seed = DatabaseFixture.CreateContext())
        {
            var order = Order(1, Owner);
            order.BillChargeAmount = 3m;
            order.BillingCreditAmount = 2m;
            order.TotalPaid = 4m;
            seed.Orders.Add(order);
            seed.Orders.Add(Order(2, Owner, Instant.AddMinutes(-1)));
            order.Items = [new OrderItem
            {
                Id = Guid.NewGuid(), OrderId = order.Id, ProductName = "History fixture", Quantity = 1,
                UnitPrice = 17m, ItemTotal = 17m, CreatedBy = nameof(CustomerOrderHistoryTests)
            }];
            await seed.SaveChangesAsync();
            var snapshot = OrderBillingSnapshotFactory.Build(order, "CHF", null, null, 20);
            seed.OrderBillingSnapshots.Add(snapshot.Header);
            seed.OrderBillingSnapshotUnits.AddRange(snapshot.Units);
            seed.OrderBillingSnapshotOwnerLinks.AddRange(snapshot.OwnerLinks);
            await seed.SaveChangesAsync();
        }
        var items = (await ReadAsync("?schemaVersion=1")).GetProperty("items").EnumerateArray().ToArray();
        items[0].GetProperty("currency").GetString().Should().Be("CHF");
        items[0].GetProperty("payableTotal").GetDecimal().Should().Be(18m);
        items[0].GetProperty("paymentStatus").GetString().Should().Be("Pending");
        items[1].GetProperty("currency").ValueKind.Should().Be(JsonValueKind.Null);
        items[1].GetProperty("payableTotal").ValueKind.Should().Be(JsonValueKind.Null);
        (await ReadAsync("?schemaVersion=1&beforeOrderDate=" + Uri.EscapeDataString(Instant.ToString("O"))
            + "&beforeOrderId=" + Id(1))).GetProperty("items").EnumerateArray()
            .Select(item => item.GetProperty("orderId").GetGuid()).Should().Equal(Id(2));
    }

    [Theory]
    [InlineData("none")]
    [InlineData("malformed")]
    [InlineData("expired")]
    [InlineData("staff")]
    public async Task Real_authentication_and_customer_role_are_required(string mode)
    {
        Client.DefaultRequestHeaders.Authorization = mode switch
        {
            "none" => null,
            "malformed" => new AuthenticationHeaderValue("Bearer", "bad-token"),
            _ => new AuthenticationHeaderValue("Bearer", CustomerBasketTokens.Create(Factory.Services,
                mode == "staff" ? UserRole.Cashier : UserRole.Customer, userId: Owner.ToString(), expired: mode == "expired"))
        };
        (await Client.GetAsync(Path + "?schemaVersion=1&userId=" + Owner)).StatusCode.Should().Be(
            mode == "staff" ? HttpStatusCode.Forbidden : HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("deleted")]
    [InlineData("staff")]
    public async Task Current_database_customer_is_required_and_empty_owned_history_remains_distinct(string mode)
    {
        AuthenticateCustomer();
        (await ReadAsync("?schemaVersion=1")).GetProperty("items").GetArrayLength().Should().Be(0);
        await using (var update = DatabaseFixture.CreateContext())
        {
            var user = await update.Users.SingleAsync(value => value.Id == Owner);
            if (mode == "missing") update.Users.Remove(user);
            else if (mode == "deleted") user.IsDeleted = true;
            else user.Role = UserRole.Admin;
            await update.SaveChangesAsync();
        }
        (await Client.GetAsync(Path + "?schemaVersion=1")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData("missing-version", "")]
    [InlineData("zero-version", "?schemaVersion=0")]
    [InlineData("future-version", "?schemaVersion=2")]
    [InlineData("missing-cursor-id", "?schemaVersion=1&beforeOrderDate=2026-10-19T12:00:00Z")]
    [InlineData("missing-cursor-date", "?schemaVersion=1&beforeOrderId=00000001-0000-4000-8000-000000000000")]
    [InlineData("malformed-cursor-date", "?schemaVersion=1&beforeOrderDate=bad&beforeOrderId=00000001-0000-4000-8000-000000000000")]
    [InlineData("empty-cursor-id", "?schemaVersion=1&beforeOrderDate=2026-10-19T12:00:00Z&beforeOrderId=00000000-0000-0000-0000-000000000000")]
    public async Task Invalid_versions_and_incomplete_cursors_are_refused_with_a_valid_positive_control(string scenario, string query)
    {
        AuthenticateCustomer();
        (await Client.GetAsync(Path + query)).StatusCode.Should().Be(HttpStatusCode.BadRequest, scenario);
        (await Client.GetAsync(Path + "?schemaVersion=1")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private void AuthenticateCustomer() => Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
        CustomerBasketTokens.Create(Factory.Services, UserRole.Customer, userId: Owner.ToString()));
    private async Task<JsonElement> ReadAsync(string query)
    {
        using var response = await Client.GetAsync(Path + query);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        var raw = await response.Content.ReadAsStringAsync();
        raw.Should().NotContain("history-private").And.NotContain("GuestStatusToken").And.NotContain("quickActionToken")
            .And.NotContain("userId").And.NotContain("stripe");
        return (await response.Content.ReadFromJsonAsync<ApiResponse<JsonElement>>())!.Data;
    }
    private static string Cursor(JsonElement cursor) => "?schemaVersion=1&beforeOrderDate="
        + Uri.EscapeDataString(cursor.GetProperty("orderDate").GetString()!) + "&beforeOrderId="
        + cursor.GetProperty("orderId").GetGuid();
    private static Guid Id(int index) => Guid.Parse($"{index:x8}-0000-4000-8000-000000000000");
    private static Order Order(int index, Guid? owner, DateTime? date = null) => new()
    {
        Id = Id(index),
        UserId = owner,
        OrderNumber = $"HISTORY-{index}",
        OrderDate = date ?? Instant,
        Type = OrderType.Takeaway,
        Status = OrderStatus.Pending,
        PaymentStatus = PaymentStatus.Pending,
        SubTotal = 17m,
        Total = 17m,
        CustomerName = "history-private",
        CustomerEmail = "history-private@example.test",
        CustomerPhone = "+41000000000",
        Notes = "history-private",
        CreatedBy = nameof(CustomerOrderHistoryTests)
    };
}
