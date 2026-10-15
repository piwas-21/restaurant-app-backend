using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

[Collection("Database Lane 3")]
public sealed class OrderPreparationHttpContractTests(DatabaseFixture databaseFixture)
    : IntegrationTestBase(databaseFixture)
{
    [Theory]
    [InlineData(0)]
    [InlineData(18)]
    public async Task Pending_guest_order_exposes_frozen_prep_before_acceptance_without_ready_time(
        int preparationMinutes)
    {
        Guid productId;
        await using (var context = DatabaseFixture.CreateContext())
        {
            var product = await context.Products
                .Where(candidate => candidate.IsActive && candidate.IsAvailable)
                .OrderBy(candidate => candidate.Id)
                .FirstAsync();
            product.PreparationTimeMinutes = preparationMinutes;
            productId = product.Id;
            await context.SaveChangesAsync();
        }

        using var response = await Client.PostAsJsonAsync("/api/orders", new
        {
            type = "Takeaway",
            customerName = "Preparation Contract Guest",
            items = new[] { new { productId, quantity = 1 } }
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var created = (await response.Content.ReadFromJsonAsync<ApiResponse<OrderDto>>(JsonOptions))!.Data!;
        created.Status.Should().Be(nameof(OrderStatus.Pending));
        created.EffectivePreparationMinutes.Should().Be(preparationMinutes);
        created.ExpectedReadyAt.Should().BeNull("the restaurant has not accepted this order");

        using var guestStatusResponse = await Client.GetAsync(
            $"/api/orders/guest-status?orderId={created.Id}&token={Uri.EscapeDataString(created.GuestStatusToken!)}");
        guestStatusResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var guestStatus = (await guestStatusResponse
            .Content.ReadFromJsonAsync<ApiResponse<GuestOrderStatusDto>>(JsonOptions))!.Data!;
        guestStatus.EffectivePreparationMinutes.Should().Be(preparationMinutes);
        guestStatus.ExpectedReadyAt.Should().BeNull();

        await using var verify = DatabaseFixture.CreateContext();
        var stored = await verify.Orders
            .Include(order => order.Items)
            .SingleAsync(order => order.Id == created.Id);
        stored.EffectivePreparationMinutes.Should().Be(preparationMinutes);
        stored.ExpectedReadyAt.Should().BeNull();
        stored.Items.Should().ContainSingle()
            .Which.PreparationTimeMinutesSnapshot.Should().Be(preparationMinutes);
    }
}
