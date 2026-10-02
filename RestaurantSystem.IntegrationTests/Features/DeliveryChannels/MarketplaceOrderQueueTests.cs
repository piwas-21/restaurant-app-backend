using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.DeliveryChannels;

[Collection("Database Lane 2")]
public sealed class MarketplaceOrderQueueTests(DatabaseFixture fixture) : ExternalOrderTestBase(fixture)
{
    [Theory]
    [InlineData(UserRole.Admin)]
    [InlineData(UserRole.Cashier)]
    [InlineData(UserRole.Server)]
    [InlineData(UserRole.KitchenStaff)]
    public async Task StaffMarketplaceQueueFiltersBeforeCountingAndPaging(UserRole role)
    {
        var request = await PrepareAsync();
        var first = await ImportAsync(request);
        var second = await ImportAsync(request with { ExternalOrderId = "provider-order-2", DisplayId = "OTHER-12" });
        await using (var context = DatabaseFixture.CreateContext())
        {
            for (var index = 0; index < 55; index++)
                context.Orders.Add(new Order
                {
                    OrderNumber = $"LOCAL-{index}",
                    OrderDate = DateTime.UtcNow,
                    CreatedBy = "test",
                    Status = OrderStatus.PendingApproval,
                    Type = OrderType.Delivery,
                    PaymentStatus = PaymentStatus.Pending,
                    Total = 5,
                    SubTotal = 5,
                    RemainingAmount = 5
                });
            await context.SaveChangesAsync();
        }
        Client.DefaultRequestHeaders.Authorization = null;
        AuthenticateAsRole(role);
        var page = await Read("/api/orders?scope=Operational&marketplaceOnly=true&pageSize=1");
        page.TotalCount.Should().Be(2); page.Items.Should().ContainSingle();
        page.Items.Single().ExternalOrder.Should().NotBeNull();
        var searched = await Read("/api/orders?scope=Operational&marketplaceOnly=true&search=9116d");
        searched.TotalCount.Should().Be(1); searched.Items.Single().Id.Should().Be(first.OrderId);
        var opposite = await Read("/api/orders?scope=Operational&marketplaceOnly=true&search=LOCAL-");
        opposite.TotalCount.Should().Be(0);
        var all = await Read("/api/orders?scope=Operational&pageSize=100");
        all.TotalCount.Should().Be(57); all.Items.Select(row => row.Id).Should().Contain(second.OrderId);
    }

    [Fact]
    public async Task GuestCannotUseMarketplaceFilterToReadProviderCustomerDetails()
    {
        var imported = await ImportAsync(await PrepareAsync());
        Client.DefaultRequestHeaders.Authorization = null;
        AuthenticateAsUser();
        var page = await Read("/api/orders?scope=Operational&marketplaceOnly=true");
        page.TotalCount.Should().Be(0); page.Items.Should().BeEmpty();
        AuthenticateAsAnonymous();
        (await Client.GetAsync("/api/orders?marketplaceOnly=true")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        imported.OrderId.Should().NotBeEmpty();
    }

    private async Task<PagedResult<OrderDto>> Read(string path)
    {
        var reply = await Client.GetAsync(path);
        reply.StatusCode.Should().Be(HttpStatusCode.OK, await reply.Content.ReadAsStringAsync());
        return (await reply.Content.ReadFromJsonAsync<ApiResponse<PagedResult<OrderDto>>>(JsonOptions))!.Data!;
    }
}
