using System.Net;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RestaurantSystem.Api.Features.Basket.Dtos;
using RestaurantSystem.Api.Features.Basket.Dtos.Requests;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

[Collection("Database Lane 4")]
public sealed class CustomerCheckoutSideTests(DatabaseFixture fixture) : CustomerCheckoutFixture(fixture)
{
    private Guid _sideId;
    private Guid _associationId;
    protected override async Task SeedTestData()
    {
        await base.SeedTestData();
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var side = new Product { Name = "Selected fries", BasePrice = 3m, IsActive = true, IsAvailable = true, CreatedBy = "quote-fixture" };
        var association = new ProductSideItem
        {
            MainProductId = ProductId,
            SideItemProduct = side,
            SideItemProductId = side.Id,
            CreatedBy = "quote-fixture"
        };
        context.ProductSideItems.Add(association);
        await context.SaveChangesAsync();
        (_sideId, _associationId) = (side.Id, association.Id);
    }

    private async Task<BasketDto> PrepareAsync() => await BasketAsync(await SwitchAsync(Channel(
        await BasketAsync(await SendAsync("/api/customer-mobile/basket/items", new AddToBasketDto
        {
            OperationId = Guid.NewGuid(),
            ProductId = ProductId,
            Quantity = 2,
            SelectedSideItems = [new() { Id = _sideId, SuggestedSideItemId = _associationId, Quantity = 2 }]
        })))));

    [Fact]
    public async Task Side_quantities_are_per_parent_and_reprice_from_the_current_product()
    {
        var basket = await PrepareAsync();
        var first = await QuoteAsync(Review(basket, 0));
        first.Money.SubTotal.Should().Be(26m); // (7 + 2 * 3) * 2.
        first.Items[0].Children.Should().ContainSingle().Which.Quantity.Should().Be(2);
        first.Items[0].Children[0].ItemTotal.Should().Be(0m);
        using (var scope = Factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            (await context.Products.SingleAsync(row => row.Id == _sideId)).BasePrice = 4m;
            await context.SaveChangesAsync();
        }
        (await QuoteResponseAsync(Review(basket, 0))).StatusCode.Should().Be(HttpStatusCode.Conflict);
        var changed = await QuoteAsync(Review(await ReadAsync(), 0));
        changed.Money.SubTotal.Should().Be(30m); // (7 + 2 * 4) * 2.
        changed.QuoteFingerprint.Should().NotBe(first.QuoteFingerprint);
        await AssertNoFinancialEffectsAsync();
    }

    [Fact]
    public async Task Flat_sides_with_optional_groups_keep_empty_answers_but_new_required_choices_are_refused()
    {
        using (var scope = Factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            context.ProductCustomizationGroups.Add(new()
            {
                ProductId = _sideId,
                Name = "Optional side extras",
                IsActive = true,
                MinSelection = 0,
                MaxSelection = 1,
                CreatedBy = "quote-fixture"
            });
            await context.SaveChangesAsync();
        }
        var basket = await PrepareAsync();
        (await QuoteAsync(Review(basket, 0))).Money.Total.Should().Be(26m);
        using (var scope = Factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            (await context.ProductCustomizationGroups.SingleAsync(group => group.ProductId == _sideId)).MinSelection = 1;
            await context.SaveChangesAsync();
        }
        (await QuoteResponseAsync(Review(basket))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        await AssertNoFinancialEffectsAsync();
    }

    [Fact]
    public async Task Removed_associations_are_refused_even_when_the_side_product_still_exists()
    {
        var basket = await PrepareAsync();
        await QuoteAsync(Review(basket));
        using (var scope = Factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            context.ProductSideItems.Remove(await context.ProductSideItems.SingleAsync(row => row.Id == _associationId));
            await context.SaveChangesAsync();
        }
        (await QuoteResponseAsync(Review(basket))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        await AssertNoFinancialEffectsAsync();
    }

    [Fact]
    public async Task Deleted_side_products_cannot_disappear_through_a_fresh_basket_display()
    {
        var basket = await PrepareAsync();
        await QuoteAsync(Review(basket));
        using (var scope = Factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            (await context.Products.SingleAsync(row => row.Id == _sideId)).IsDeleted = true;
            await context.SaveChangesAsync();
        }
        var incomplete = await ReadAsync();
        incomplete.Items[0].SelectedSideItems.Should().BeEmpty();
        (await QuoteResponseAsync(Review(incomplete))).StatusCode.Should().Be(HttpStatusCode.Conflict);
        await AssertNoFinancialEffectsAsync();
    }

    [Fact]
    public async Task A_newly_required_side_cannot_be_omitted_by_a_preexisting_basket()
    {
        var basket = await CheckoutBasketAsync();
        await QuoteAsync(Review(basket));
        using (var scope = Factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            (await context.ProductSideItems.SingleAsync(row => row.Id == _associationId)).IsRequired = true;
            await context.SaveChangesAsync();
        }
        (await QuoteResponseAsync(Review(basket))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        await AssertNoFinancialEffectsAsync();
    }
}
