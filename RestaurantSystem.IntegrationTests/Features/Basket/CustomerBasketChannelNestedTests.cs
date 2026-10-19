using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Basket;

[Collection("Database Lane 2")]
public sealed class CustomerBasketChannelNestedTests(DatabaseFixture fixture) : CustomerBasketChannelFixture(fixture)
{
    [Fact]
    public async Task Preview_and_confirmed_switch_treat_an_incompatible_child_as_part_of_its_root()
    {
        var basket = await AddAsync();
        using (var scope = Factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var product = new Product
            {
                Name = "Incompatible nested fixture",
                BasePrice = 1m,
                IsActive = true,
                IsAvailable = true,
                AvailableOrderTypes = (int)OrderChannels.DineIn,
                CreatedBy = nameof(CustomerBasketChannelNestedTests)
            };
            context.Products.Add(product);
            context.BasketItems.Add(new BasketItem
            {
                BasketId = basket.Id,
                ParentBasketItemId = basket.Items[0].Id,
                Product = product,
                Quantity = 2,
                UnitPrice = 1m,
                ItemTotal = 2m,
                CreatedBy = nameof(CustomerBasketChannelNestedTests)
            });
            await context.SaveChangesAsync();
        }
        var current = await ReadAsync();
        current.Items.Single().ChildItems.Should().ContainSingle();
        var preview = await PreviewAsync();
        preview.Conflicts.Should().ContainSingle().Which.BasketItemId.Should().Be(current.Items[0].Id!.Value);
        preview.Conflicts[0].ProductName.Should().Be("Native mutation fixture");
        var result = await BasketAsync(await SwitchAsync(Channel(current, removals: [current.Items[0].Id!.Value])));
        result.Items.Should().BeEmpty();
        result.OrderType.Should().Be(OrderType.Takeaway);
        using var verify = Factory.Services.CreateScope();
        (await verify.ServiceProvider.GetRequiredService<ApplicationDbContext>().BasketItems
            .CountAsync(item => item.BasketId == basket.Id)).Should().Be(0, "no child may be promoted after its confirmed root is removed");
    }
}
