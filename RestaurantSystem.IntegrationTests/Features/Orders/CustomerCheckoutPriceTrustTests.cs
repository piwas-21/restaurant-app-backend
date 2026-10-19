using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

[Collection("Database Lane 4")]
public sealed class CustomerCheckoutPriceTrustTests(DatabaseFixture fixture) : CustomerCheckoutCompositionFixture(fixture)
{
    [Fact]
    public void Signed_component_marker_is_neither_read_nor_written_by_request_json()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var dto = JsonSerializer.Deserialize<CreateOrderItemDto>(
            """{"nativeBundlePriceVerified":true,"unitPrice":-2,"kind":0}""", options)!;
        dto.NativeBundlePriceVerified.Should().BeFalse();
        var serialized = JsonSerializer.SerializeToElement(dto with { NativeBundlePriceVerified = true }, options);
        serialized.TryGetProperty("nativeBundlePriceVerified", out _).Should().BeFalse();
    }

    [Theory]
    [InlineData(false, OrderItemKind.BundleChild, true, 7)]
    [InlineData(true, OrderItemKind.SideItem, true, 7)]
    [InlineData(true, OrderItemKind.BundleChild, true, -2)]
    [InlineData(true, OrderItemKind.BundleChild, false, 7)]
    public async Task Only_verified_server_priced_bundle_children_keep_signed_prices(
        bool verified, OrderItemKind kind, bool isChild, decimal expectedPrice)
    {
        using var scope = Factory.Services.CreateScope();
        var factory = scope.ServiceProvider.GetRequiredService<IOrderItemFactory>();
        var component = new CreateOrderItemDto
        {
            ProductId = ProductId,
            Quantity = 1,
            UnitPrice = -2m,
            NativeBundlePriceVerified = verified,
            Kind = kind
        };
        var root = isChild ? new CreateOrderItemDto
        {
            ProductId = _extraId,
            Quantity = 1,
            UnitPrice = 19m,
            ChildItems = [component]
        } : component;
        var order = new Order { CreatedBy = "quote-price-trust" };
        (await factory.AddItemAsync(order, root, true, CancellationToken.None, allowStaffPrices: false)).Should().BeNull();
        order.Items.Single(item => item.ProductId == ProductId).UnitPrice.Should().Be(expectedPrice);
    }

    [Fact]
    public async Task Internal_marker_cannot_make_an_untrusted_root_price_authoritative()
    {
        using var scope = Factory.Services.CreateScope();
        var factory = scope.ServiceProvider.GetRequiredService<IOrderItemFactory>();
        var order = new Order { CreatedBy = "quote-price-trust" };
        var dto = new CreateOrderItemDto
        {
            ProductId = ProductId,
            Quantity = 1,
            UnitPrice = -2m,
            NativeBundlePriceVerified = true,
            Kind = OrderItemKind.BundleChild
        };
        (await factory.AddItemAsync(order, dto, false, CancellationToken.None, allowStaffPrices: false)).Should().BeNull();
        order.Items.Should().ContainSingle().Which.UnitPrice.Should().Be(7m);
    }
}
