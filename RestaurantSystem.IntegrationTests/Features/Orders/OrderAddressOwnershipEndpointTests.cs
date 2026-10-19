using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.IntegrationTests.Common;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

[Collection("Database Lane 3")]
public sealed class OrderAddressOwnershipEndpointTests(DatabaseFixture database) : IntegrationTestBase(database)
{
    protected override void ConfigureTestServices(IServiceCollection services)
    {
        services.RemoveAll<IOrderNotificationService>();
        services.AddScoped(_ => Mock.Of<IOrderNotificationService>());
    }
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Direct_and_basket_checkout_refuse_foreign_saved_address_without_order_or_snapshot(bool fromBasket, bool guest)
    {
        var (productId, addressId) = await SeedAsync(Guid.NewGuid(), "Foreign private street");
        var response = await PlaceAsync(fromBasket, guest, productId, new { useAddressId = addressId });
        response.StatusCode.Should().Be(guest ? HttpStatusCode.Forbidden : HttpStatusCode.NotFound,
            await response.Content.ReadAsStringAsync());
        (await response.Content.ReadAsStringAsync()).Should().NotContain("Foreign private street");
        await using var read = DatabaseFixture.CreateContext();
        (await read.Orders.CountAsync()).Should().Be(0);
        (await read.OrderAddresses.CountAsync()).Should().Be(0);
        if (fromBasket) (await read.BasketItems.CountAsync()).Should().Be(1);
    }
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Direct_and_basket_checkout_preserve_owned_saved_and_inline_guest_addresses(bool fromBasket, bool guest)
    {
        var (productId, addressId) = await SeedAsync(Guid.Parse(TestAuthHandler.UserId), "Owned street");
        var response = await PlaceAsync(fromBasket, guest, productId, new
        {
            useAddressId = guest ? (Guid?)null : addressId,
            addressLine1 = guest ? "Inline guest street" : null,
            city = "Fixture city",
            postalCode = "1000",
            country = "CH"
        });
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        await using var read = DatabaseFixture.CreateContext();
        (await read.Orders.CountAsync()).Should().Be(1);
        var snapshot = await read.OrderAddresses.SingleAsync();
        snapshot.AddressLine1.Should().Be(guest ? "Inline guest street" : "Owned street");
        snapshot.UserAddressId.Should().Be(guest ? null : addressId);
    }
    private async Task<(Guid ProductId, Guid AddressId)> SeedAsync(Guid owner, string street)
    {
        await using var context = DatabaseFixture.CreateContext();
        await TestUserSeeder.SeedUserAsync(context, owner);
        var address = OrderAddressOwnershipTests.Address(owner, street);
        context.UserAddresses.Add(address);
        var channel = await context.OrderTypeConfigurations.SingleAsync(value => value.OrderType == OrderType.Delivery);
        channel.IsEnabled = true; channel.EnforceOpeningHours = false;
        var productId = await context.Products.Where(value => value.IsActive && value.IsAvailable)
            .Select(value => value.Id).FirstAsync();
        await context.SaveChangesAsync();
        return (productId, address.Id);
    }
    private async Task<HttpResponseMessage> PlaceAsync(bool fromBasket, bool guest, Guid productId, object address)
    {
        if (guest) AuthenticateAsAnonymous(); else AuthenticateAsUser();
        Client.DefaultRequestHeaders.Add("X-Session-Id", Guid.NewGuid().ToString("D"));
        if (fromBasket)
        {
            var add = await Client.PostAsJsonAsync("/api/basket/items", new { productId, quantity = 1 });
            add.StatusCode.Should().Be(HttpStatusCode.OK, await add.Content.ReadAsStringAsync());
        }
        return await Client.PostAsJsonAsync(fromBasket ? "/api/orders/from-basket" : "/api/orders", new
        {
            type = "Delivery",
            customerName = "Fixture guest",
            deliveryAddress = address,
            items = new[] { new { productId, quantity = 1 } }
        });
    }
}
