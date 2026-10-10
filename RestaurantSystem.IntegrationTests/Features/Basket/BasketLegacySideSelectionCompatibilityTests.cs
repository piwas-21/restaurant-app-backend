using System.Net;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Basket.Dtos;
using RestaurantSystem.Api.Features.Basket.Dtos.Requests;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Basket;

[Collection("Database Lane 1")]
public sealed class BasketLegacySideSelectionCompatibilityTests(DatabaseFixture databaseFixture)
    : IntegrationTestBase(databaseFixture)
{
    private Product _pizza = null!;
    private Product _cola = null!;
    private Product _fries = null!;

    protected override async Task SeedTestData()
    {
        await base.SeedTestData();
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        _pizza = await context.Products.SingleAsync(product => product.Name == "Test Pizza");
        _cola = await context.Products.SingleAsync(product => product.Name == "Test Cola");
        _fries = new Product
        {
            Id = Guid.NewGuid(),
            Name = "Test Fries",
            BasePrice = 4m,
            IsActive = true,
            IsAvailable = true,
            Type = ProductType.MainItem,
            Ingredients = [],
            Allergens = [],
            CreatedAt = DateTime.UtcNow,
            CreatedBy = nameof(BasketLegacySideSelectionCompatibilityTests)
        };
        context.Products.Add(_fries);
        await context.SaveChangesAsync();
    }

    [Fact]
    public async Task Legacy_side_without_an_association_keeps_server_priced_unknown_metadata()
    {
        Client.DefaultRequestHeaders.Add("X-Session-Id", Guid.NewGuid().ToString());
        var response = await PostAsJsonAsync("/api/basket/items", RequestFor(_cola.Id));

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var basket = (await ReadResponseAsync<ApiResponse<BasketDto>>(response))!.Data!;
        var side = basket.Items.Single().SelectedSideItems!.Single();
        side.Id.Should().Be(_cola.Id);
        side.SuggestedSideItemId.Should().BeNull();
        side.CompositionRole.Should().Be(CompositionRole.Unknown);
        side.Price.Should().Be(_cola.BasePrice);
    }

    [Fact]
    public async Task Configured_products_reject_foreign_sides_and_invalid_explicit_association_ids()
    {
        using (var scope = Factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            context.ProductSideItems.Add(new ProductSideItem
            {
                Id = Guid.NewGuid(),
                MainProductId = _pizza.Id,
                SideItemProductId = _cola.Id,
                IsRequired = false,
                DisplayOrder = 1,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = nameof(BasketLegacySideSelectionCompatibilityTests)
            });
            await context.SaveChangesAsync();
        }

        Client.DefaultRequestHeaders.Add("X-Session-Id", Guid.NewGuid().ToString());
        var invalidAssociation = await PostAsJsonAsync("/api/basket/items", RequestFor(_cola.Id) with
        {
            SelectedSideItems = [new SelectedSideItemDto
            {
                Id = _cola.Id,
                SuggestedSideItemId = Guid.NewGuid(),
                Quantity = 1
            }]
        });
        invalidAssociation.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await invalidAssociation.Content.ReadAsStringAsync()).Should()
            .Contain("A selected side is stale or ambiguous");

        var foreignSide = await PostAsJsonAsync("/api/basket/items", RequestFor(_fries.Id));
        foreignSide.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await foreignSide.Content.ReadAsStringAsync()).Should()
            .Contain("A selected side is stale or ambiguous");
    }

    private AddToBasketDto RequestFor(Guid sideProductId) => new()
    {
        ProductId = _pizza.Id,
        Quantity = 1,
        SelectedSideItems = [new SelectedSideItemDto { Id = sideProductId, Quantity = 1 }]
    };
}
