using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.IntegrationTests.Common;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

[Collection("Database Lane 2")]
public class GuestOrderOpeningHoursTests(DatabaseFixture databaseFixture) : IntegrationTestBase(databaseFixture)
{
    private readonly MutableClock _clock = new(
        new DateTimeOffset(2030, 5, 17, 11, 57, 0, TimeSpan.Zero), "Europe/Paris");

    protected override void ConfigureTestServices(IServiceCollection services) =>
        services.AddSingleton<ITenantClock>(_clock);

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Closed_guest_orders_are_rejected_without_acceptance_or_basket_loss(bool fromBasket, bool authenticated)
    {
        var productId = await ConfigureAsync(enforceHours: true);
        if (authenticated) AuthenticateAsUser();
        else AuthenticateAsAnonymous();
        Client.DefaultRequestHeaders.Add("X-Session-Id", Guid.NewGuid().ToString());
        if (fromBasket)
        {
            var add = await Client.PostAsJsonAsync("/api/basket/items", new { productId, quantity = 1 });
            add.StatusCode.Should().Be(HttpStatusCode.OK, await add.Content.ReadAsStringAsync());
        }
        var response = fromBasket
            ? await Client.PostAsJsonAsync("/api/orders/from-basket", new { type = "Takeaway", customerName = "Guest" })
            : await PlaceDirectAsync(productId);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest, await response.Content.ReadAsStringAsync());
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<OrderDto>>(JsonOptions);
        body!.Success.Should().BeFalse();
        body.ErrorCode.Should().Be(ErrorCodes.OrderTypeNotAvailable);
        await using var context = DatabaseFixture.CreateContext();
        (await context.Orders.AnyAsync()).Should().BeFalse();
        (await context.OrderPayments.AnyAsync()).Should().BeFalse();
        (await context.OrderNumberSequences.AnyAsync()).Should().BeFalse("rejection precedes number allocation");
        if (fromBasket) (await context.BasketItems.CountAsync()).Should().Be(1);
    }

    [Theory]
    [InlineData(10, 0)]
    [InlineData(16, 0)]
    public async Task Guest_orders_are_accepted_in_each_open_split_shift(int utcHour, int utcMinute)
    {
        var productId = await ConfigureAsync(enforceHours: true);
        _clock.Set(new DateTimeOffset(2030, 5, 17, utcHour, utcMinute, 0, TimeSpan.Zero), "Europe/Paris");
        AuthenticateAsAnonymous();
        await AssertAcceptedAsync(productId);
    }

    [Fact]
    public async Task Explicit_hours_opt_out_still_accepts_a_guest_order()
    {
        var productId = await ConfigureAsync(enforceHours: false);
        AuthenticateAsAnonymous();
        await AssertAcceptedAsync(productId);
    }

    [Theory]
    [InlineData(UserRole.Admin)]
    [InlineData(UserRole.Cashier)]
    [InlineData(UserRole.Server)]
    public async Task Staff_can_accept_an_order_during_closed_hours(UserRole role)
    {
        var productId = await ConfigureAsync(enforceHours: true);
        AuthenticateAsRole(role);
        await AssertAcceptedAsync(productId);
    }

    [Fact]
    public async Task A_disabled_channel_cannot_be_reached_through_direct_create()
    {
        var productId = await ConfigureAsync(enforceHours: false);
        await using (var context = DatabaseFixture.CreateContext())
        {
            var channel = await context.OrderTypeConfigurations.SingleAsync(c => c.OrderType == OrderType.Takeaway);
            channel.IsEnabled = false;
            await context.SaveChangesAsync();
        }
        AuthenticateAsAnonymous();
        var response = await PlaceDirectAsync(productId);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain(ErrorCodes.OrderTypeNotAvailable);
    }

    private async Task<Guid> ConfigureAsync(bool enforceHours)
    {
        await using var context = DatabaseFixture.CreateContext();
        context.Users.Add(new ApplicationUser
        {
            Id = Guid.Parse(TestAuthHandler.StaffUserId),
            UserName = TestAuthHandler.StaffUserName,
            Email = TestAuthHandler.StaffUserName,
            FirstName = "Test",
            LastName = "Staff",
            Role = UserRole.Server,
            RefreshToken = string.Empty,
            CreatedBy = "test"
        });
        var channel = await context.OrderTypeConfigurations.SingleAsync(c => c.OrderType == OrderType.Takeaway);
        channel.IsEnabled = true;
        channel.EnforceOpeningHours = enforceHours;
        context.WorkingHours.Add(new WorkingHours
        {
            DayOfWeek = DayOfWeek.Friday,
            IsActive = true,
            IsClosed = false,
            OpenTime = new TimeSpan(11, 30, 0),
            CloseTime = new TimeSpan(13, 30, 0),
            CreatedBy = "test",
            Shifts =
            [
                new() { OpenTime = new TimeSpan(11, 30, 0), CloseTime = new TimeSpan(13, 30, 0), CreatedBy = "test" },
                new() { OpenTime = new TimeSpan(18, 0, 0), CloseTime = new TimeSpan(21, 30, 0), CreatedBy = "test" }
            ]
        });
        await context.SaveChangesAsync();
        _clock.Now.TimeOfDay.Should().Be(new TimeSpan(13, 57, 0), "this reproduces the reported local closure");
        return await context.Products.Where(p => p.IsActive && p.IsAvailable).Select(p => p.Id).FirstAsync();
    }

    private Task<HttpResponseMessage> PlaceDirectAsync(Guid productId) =>
        Client.PostAsJsonAsync("/api/orders", new
        {
            type = "Takeaway",
            customerName = "Guest",
            items = new[] { new { productId, quantity = 1 } }
        });

    private async Task AssertAcceptedAsync(Guid productId)
    {
        var response = await PlaceDirectAsync(productId);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<OrderDto>>(JsonOptions);
        body!.Success.Should().BeTrue(body.Message);
        body.Data.Should().NotBeNull();
        await using var context = DatabaseFixture.CreateContext();
        (await context.Orders.CountAsync()).Should().Be(1);
    }
}
