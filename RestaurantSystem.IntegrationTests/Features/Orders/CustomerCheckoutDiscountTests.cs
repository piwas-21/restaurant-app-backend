using System.Net;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RestaurantSystem.Api.Settings;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Common;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

[Collection("Database Lane 4")]
public sealed class CustomerCheckoutDiscountTests(DatabaseFixture fixture) : CustomerCheckoutFixture(fixture)
{
    protected override void ConfigureTestServices(IServiceCollection services)
    {
        base.ConfigureTestServices(services);
        services.PostConfigure<CustomerMobileSettings>(settings => settings.EnabledCapabilities = ["ordering", "loyalty"]);
        services.PostConfigure<OrderSettings>(settings => settings.DeliveryFee = 4m);
    }

    protected override async Task SeedTestData()
    {
        await base.SeedTestData();
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var userId = Guid.Parse(TestAuthHandler.UserId);
        context.CustomerDiscountRules.Add(new()
        {
            UserId = userId,
            Name = "Limited quote discount",
            DiscountType = DiscountType.Percentage,
            DiscountValue = 20m,
            MaxUsageCount = 1,
            IsActive = true,
            CreatedBy = "quote-fixture"
        });
        context.FidelityPointBalances.Add(new()
        {
            UserId = userId,
            CurrentPoints = 500,
            TotalEarnedPoints = 500,
            CreatedBy = "quote-fixture"
        });
        await context.SaveChangesAsync();
        AuthenticateAsUser();
    }

    [Fact]
    public async Task Limited_customer_discount_and_points_are_previewed_repeatedly_without_consumption()
    {
        var basket = await CheckoutBasketAsync();
        var review = Review(basket) with { PointsToRedeem = 100 };
        var first = await QuoteAsync(review);
        var second = await QuoteAsync(review);
        first.Should().BeEquivalentTo(second);
        first.Money.SubTotal.Should().Be(14m);
        first.Money.CustomerDiscount.Should().Be(2.8m);
        first.Money.PointsDiscount.Should().Be(1m);
        first.Money.PointsRedeemed.Should().Be(100);
        // Existing courtesy rounding: ceil(14 - 2.8) - 1 point credit + 3 chosen tip.
        first.Money.Total.Should().Be(14m);
        first.Money.Tip.Should().Be(3m);
        await AssertNoFinancialEffectsAsync();
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var discount = await context.CustomerDiscountRules.AsNoTracking().SingleAsync();
        discount.UsageCount.Should().Be(0);
        discount.IsActive.Should().BeTrue();
        (await context.FidelityPointBalances.AsNoTracking().SingleAsync()).CurrentPoints.Should().Be(500);
    }

    [Fact]
    public async Task Delivery_fee_is_server_owned_and_card_at_the_till_is_not_offered_for_delivery()
    {
        var basket = await CheckoutBasketAsync(OrderType.Delivery);
        var quote = await QuoteAsync(Review(basket));
        quote.Money.DeliveryFee.Should().Be(4m);
        quote.Money.Total.Should().Be(19m); // ceil(14 + 4 - 2.8) + 3 tip.
        quote.AvailablePaymentMethods.Should().Equal(PaymentMethod.Cash);
        await AssertNoFinancialEffectsAsync();
    }

    [Fact]
    public async Task Insufficient_points_refuse_the_quote_and_balance_changes_invalidate_the_original_preview()
    {
        var basket = await CheckoutBasketAsync();
        var review = Review(basket) with { PointsToRedeem = 100 };
        await QuoteAsync(review);
        (await QuoteResponseAsync(review with { PointsToRedeem = 501 })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using (var scope = Factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            (await context.FidelityPointBalances.SingleAsync()).CurrentPoints = 99;
            await context.SaveChangesAsync();
        }
        (await QuoteResponseAsync(review)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        await AssertNoFinancialEffectsAsync();
    }
    [Fact]
    public async Task A_full_discount_cannot_conceal_an_unpersistable_live_subtotal_or_line()
    {
        var basket = await CheckoutBasketAsync();
        using (var scope = Factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            (await context.Products.SingleAsync(product => product.Id == ProductId)).BasePrice = 99_999_999.99m;
            (await context.CustomerDiscountRules.SingleAsync()).DiscountValue = 100m;
            await context.SaveChangesAsync();
        }
        // Two live units exceed decimal(10,2), despite a 100% discount and zero payable food.
        (await QuoteResponseAsync(Review(basket, 0))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        await AssertNoFinancialEffectsAsync();
    }

    [Fact]
    public async Task Percentage_discount_keeps_its_exact_scale_and_existing_courtesy_rounding()
    {
        var basket = await CheckoutBasketAsync();
        using (var scope = Factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            (await context.Products.SingleAsync(product => product.Id == ProductId)).BasePrice = 12.99m;
            await context.SaveChangesAsync();
        }
        var quote = await QuoteAsync(Review(basket));
        quote.Money.SubTotal.Should().Be(25.98m);
        quote.Money.CustomerDiscount.Should().Be(5.196m);
        quote.Money.Total.Should().Be(24m); // ceil(25.98 - 5.196) + 3 chosen tip.
        await AssertNoFinancialEffectsAsync();
    }

}
