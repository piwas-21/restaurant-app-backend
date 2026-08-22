using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Basket.Dtos.Requests;
using RestaurantSystem.Api.Features.Basket.Interfaces;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Basket;

/// <summary>
/// Track F / F2 — "hide the base product" as the ADD PATH sees it, not as the guard sees it.
/// </summary>
/// <remarks>
/// <see cref="BasketBaseProductGuardTests"/> pins the decision; this pins the WIRING, which is the
/// half that a React-only implementation of this feature would silently lack. It also exercises the
/// column end to end (migration + EF config + entity), so a flag that never reaches the database
/// fails here rather than in production.
/// </remarks>
[Collection("Database Lane 2")]
public class BasketHiddenBaseProductTests : IntegrationTestBase
{
    private readonly string _sessionId = Guid.NewGuid().ToString();
    private Guid _dessertId;
    private Guid _revaniId;

    public BasketHiddenBaseProductTests(DatabaseFixture databaseFixture)
        : base(databaseFixture)
    {
    }

    protected override async Task SeedTestData()
    {
        await base.SeedTestData();

        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var dessert = new Product
        {
            Name = "Günün tatlısı",
            BasePrice = 6.00m,
            IsActive = true,
            IsAvailable = true,
            HideBaseProduct = true,
            CreatedBy = "seed"
        };
        dessert.Variations.Add(new ProductVariation
        {
            Name = "Revani",
            PriceModifier = 0m,
            IsActive = true,
            DisplayOrder = 0,
            CreatedBy = "seed"
        });

        context.Products.Add(dessert);
        await context.SaveChangesAsync();

        _dessertId = dessert.Id;
        _revaniId = dessert.Variations.First().Id;
    }

    [Fact]
    public async Task The_add_path_refuses_a_product_whose_base_row_is_hidden()
    {
        // The stale-tab / crafted-payload shape: the radio is not on screen, the request still is.
        var thrown = await Record.ExceptionAsync(() => AddAsync(variationId: null));

        thrown.Should().BeOfType<BadRequestException>()
            .Which.ErrorCode.Should().Be(ErrorCodes.VariationRequired);
        (await LineCountAsync()).Should().Be(0, "a refused add must leave no line behind");
    }

    [Fact]
    public async Task The_add_path_accepts_the_same_product_with_a_variation()
    {
        var thrown = await Record.ExceptionAsync(() => AddAsync(_revaniId));

        thrown.Should().BeNull();
        (await LineCountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task A_product_whose_variations_are_all_inactive_stays_orderable()
    {
        // The degrade, driven through the real add path: turning off the last variation must not
        // leave the tenant with a product nobody — guest, waiter or till — can put in a basket.
        using (var scope = Factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var variation = await context.ProductVariations.FirstAsync(v => v.Id == _revaniId);
            variation.IsActive = false;
            await context.SaveChangesAsync();
        }

        var thrown = await Record.ExceptionAsync(() => AddAsync(variationId: null));

        thrown.Should().BeNull();
        (await LineCountAsync()).Should().Be(1);
    }

    private async Task AddAsync(Guid? variationId)
    {
        using var scope = Factory.Services.CreateScope();
        var basketService = scope.ServiceProvider.GetRequiredService<IBasketService>();
        await basketService.AddItemToBasketAsync(_sessionId, null, new AddToBasketDto
        {
            ProductId = _dessertId,
            ProductVariationId = variationId,
            Quantity = 1
        });
    }

    private async Task<int> LineCountAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await context.BasketItems.CountAsync(bi => bi.ProductId == _dessertId);
    }
}
