using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.IntegrationTests.Features.Basket;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

public sealed partial class CustomerOrderHistoryTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task History_detail_retains_frozen_lines_and_only_recorded_currency_without_operation_proof(bool recordedCurrency)
    {
        AuthenticateCustomer();
        await SeedHistoryDetailAsync(recordedCurrency);
        var response = await Client.GetAsync(DetailPath());
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        var raw = await response.Content.ReadAsStringAsync();
        raw.Should().NotContain("history-private").And.NotContain("userId").And.NotContain("guestStatusToken")
            .And.NotContain("payloadHash").And.NotContain("Changed menu").And.NotContain("EUR");
        using var document = JsonDocument.Parse(raw);
        var value = document.RootElement.GetProperty("data");
        value.GetProperty("orderId").GetGuid().Should().Be(Id(1));
        value.GetProperty("currency").GetString().Should().Be(recordedCurrency ? "CHF" : null);
        value.GetProperty("status").GetString().Should().Be("Preparing");
        value.GetProperty("paymentStatus").GetString().Should().Be("Pending");
        value.GetProperty("totalPaid").GetDecimal().Should().Be(4m);
        value.GetProperty("payableTotal").GetDecimal().Should().Be(18m);
        value.GetProperty("remainingAmount").GetDecimal().Should().Be(14m);
        value.GetProperty("items")[0].GetProperty("name").GetString().Should().Be("Frozen history dish");
        value.GetProperty("items")[0].GetProperty("specialInstructions").GetString().Should().Be("No salt");
        value.GetProperty("items")[0].GetProperty("ingredients")[0].GetProperty("name").GetString().Should().Be("Frozen sauce");
        await using var read = DatabaseFixture.CreateContext();
        (await read.Set<CustomerOrderOperation>().CountAsync()).Should().Be(0);
        (await read.Orders.CountAsync()).Should().Be(1);
    }

    [Theory]
    [InlineData("none")]
    [InlineData("malformed")]
    [InlineData("expired")]
    [InlineData("staff")]
    public async Task History_detail_requires_real_customer_authentication(string mode)
    {
        await SeedHistoryDetailAsync(false);
        Client.DefaultRequestHeaders.Authorization = mode switch
        {
            "none" => null,
            "malformed" => new AuthenticationHeaderValue("Bearer", "bad-token"),
            _ => new AuthenticationHeaderValue("Bearer", CustomerBasketTokens.Create(Factory.Services,
                mode == "staff" ? UserRole.Admin : UserRole.Customer, expired: mode == "expired", userId: Owner.ToString()))
        };
        (await Client.GetAsync(DetailPath())).StatusCode.Should()
            .Be(mode == "staff" ? HttpStatusCode.Forbidden : HttpStatusCode.Unauthorized);
        AuthenticateCustomer();
        (await Client.GetAsync(DetailPath())).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData("foreign")]
    [InlineData("anonymous")]
    [InlineData("deleted-order")]
    [InlineData("deleted-account")]
    [InlineData("changed-role")]
    public async Task Retained_history_details_cannot_outlive_current_customer_ownership(string mode)
    {
        AuthenticateCustomer();
        await SeedHistoryDetailAsync(false);
        (await Client.GetAsync(DetailPath())).StatusCode.Should().Be(HttpStatusCode.OK);
        await using (var update = DatabaseFixture.CreateContext())
        {
            var order = await update.Orders.SingleAsync();
            if (mode == "foreign") order.UserId = Foreign;
            else if (mode == "anonymous") order.UserId = null;
            else if (mode == "deleted-order") order.IsDeleted = true;
            else
            {
                var user = await update.Users.SingleAsync(user => user.Id == Owner);
                if (mode == "deleted-account") user.IsDeleted = true;
                else user.Role = UserRole.Admin;
            }
            await update.SaveChangesAsync();
        }
        (await Client.GetAsync(DetailPath())).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await Client.GetAsync(DetailPath(Id(2)))).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData("")]
    [InlineData("?schemaVersion=0")]
    [InlineData("?schemaVersion=2")]
    public async Task History_details_require_explicit_supported_schema_version(string query)
    {
        AuthenticateCustomer();
        await SeedHistoryDetailAsync(false);
        (await Client.GetAsync(Path + "/" + Id(1) + query)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Client.GetAsync(DetailPath())).StatusCode.Should().Be(HttpStatusCode.OK);
        (await Client.GetAsync(DetailPath(Guid.Empty))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Oversized_frozen_history_detail_is_refused_instead_of_truncated()
    {
        AuthenticateCustomer();
        await SeedHistoryDetailAsync(false);
        await using (var seed = DatabaseFixture.CreateContext())
        {
            seed.OrderItems.AddRange(Enumerable.Range(1, 1000).Select(_ => HistoryLine(Id(1))));
            await seed.SaveChangesAsync();
        }
        (await Client.GetAsync(DetailPath())).StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    private static string DetailPath(Guid? id = null) => Path + "/" + (id ?? Id(1)) + "?schemaVersion=1";
    private async Task SeedHistoryDetailAsync(bool recordedCurrency)
    {
        await using var seed = DatabaseFixture.CreateContext();
        var order = Order(1, Owner);
        order.Items = [HistoryLine(order.Id)];
        var product = await seed.Products.FirstAsync();
        order.Items.Single().ProductId = product.Id;
        product.Name = "Changed menu";
        var line = order.Items.Single();
        line.IngredientSnapshots = [new OrderItemIngredient
        {
            Id = Guid.NewGuid(), OrderItemId = line.Id, IngredientId = Guid.NewGuid(), IngredientName = "Frozen sauce",
            Quantity = 1, CreatedBy = nameof(CustomerOrderHistoryTests)
        }];
        order.Status = OrderStatus.Preparing;
        order.BillChargeAmount = 3m;
        order.BillingCreditAmount = 2m;
        order.TotalPaid = 4m;
        seed.Orders.Add(order);
        await seed.SaveChangesAsync();
        if (recordedCurrency)
        {
            var snapshot = OrderBillingSnapshotFactory.Build(order, "CHF", null, null, 20);
            seed.OrderBillingSnapshots.Add(snapshot.Header);
            seed.OrderBillingSnapshotUnits.AddRange(snapshot.Units);
            seed.OrderBillingSnapshotOwnerLinks.AddRange(snapshot.OwnerLinks);
        }
        (await seed.RestaurantInfo.SingleAsync()).Currency = "EUR";
        await seed.SaveChangesAsync();
    }
    private static OrderItem HistoryLine(Guid orderId) => new()
    {
        Id = Guid.NewGuid(),
        OrderId = orderId,
        ProductName = "Frozen history dish",
        Quantity = 1,
        UnitPrice = 17m,
        ItemTotal = 17m,
        SpecialInstructions = "No salt",
        CreatedBy = nameof(CustomerOrderHistoryTests)
    };
}
