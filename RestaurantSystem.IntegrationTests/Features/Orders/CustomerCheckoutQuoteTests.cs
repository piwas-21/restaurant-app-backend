using System.Net;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

[Collection("Database Lane 4")]
public sealed class CustomerCheckoutQuoteTests(DatabaseFixture fixture) : CustomerCheckoutFixture(fixture)
{
    [Fact]
    public async Task Repeated_live_quotes_are_stable_and_do_not_write_orders_tenders_points_or_basket_operations()
    {
        var basket = await CheckoutBasketAsync();
        var receipts = await ReceiptsAsync();
        var first = await QuoteAsync(Review(basket));
        var second = await QuoteAsync(Review(basket));
        first.Should().BeEquivalentTo(second);
        first.SchemaVersion.Should().Be(1);
        first.BasketId.Should().Be(basket.Id);
        first.PurchaseFingerprint.Should().Be(basket.PurchaseFingerprint);
        first.QuoteFingerprint.Should().MatchRegex("^[a-f0-9]{64}$");
        first.Money.SubTotal.Should().Be(14m); // Two products at 7 each; chosen tip is separate.
        first.Money.Tip.Should().Be(3m);
        first.Money.Total.Should().Be(17m);
        first.Money.Tax.Should().Be(0m);
        first.PriceChanged.Should().BeFalse();
        first.AvailablePaymentMethods.Should().Equal(PaymentMethod.Cash, PaymentMethod.CreditCard);
        first.Items.Should().ContainSingle().Which.UnitPrice.Should().Be(7m);
        first.Items[0].BasketItemId.Should().Be(basket.Items[0].Id);
        first.Items[0].Quantity.Should().Be(2);
        (await ReadAsync()).Should().BeEquivalentTo(basket);
        (await ReceiptsAsync()).Should().BeEquivalentTo(receipts);
        await AssertNoFinancialEffectsAsync();
    }

    [Fact]
    public async Task Catalogue_price_changes_change_the_quote_without_changing_the_reviewed_basket()
    {
        var basket = await CheckoutBasketAsync();
        var before = await QuoteAsync(Review(basket));
        using (var scope = Factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            (await context.Products.SingleAsync(product => product.Id == ProductId)).BasePrice = 9m;
            await context.SaveChangesAsync();
        }
        var after = await QuoteAsync(Review(basket));
        after.Money.SubTotal.Should().Be(18m);
        after.Money.Total.Should().Be(21m);
        after.Items[0].ItemTotal.Should().Be(18m);
        after.PriceChanged.Should().BeTrue();
        after.QuoteFingerprint.Should().NotBe(before.QuoteFingerprint);
        (await QuoteAsync(Review(basket))).QuoteFingerprint.Should().Be(after.QuoteFingerprint);
        (await ReadAsync()).Total.Should().Be(14m);
        await AssertNoFinancialEffectsAsync();
    }

    [Theory]
    [InlineData("inactive")]
    [InlineData("unavailable")]
    [InlineData("deleted")]
    [InlineData("other-channel")]
    public async Task Withdrawn_products_never_receive_a_payable_quote(string state)
    {
        var basket = await CheckoutBasketAsync();
        using (var scope = Factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var product = await context.Products.SingleAsync(product => product.Id == ProductId);
            if (state == "inactive") product.IsActive = false;
            if (state == "unavailable") product.IsAvailable = false;
            if (state == "deleted") product.IsDeleted = true;
            if (state == "other-channel") product.AvailableOrderTypes = (int)OrderChannels.DineIn;
            await context.SaveChangesAsync();
        }
        var response = await QuoteResponseAsync(Review(basket));
        response.StatusCode.Should().BeOneOf(HttpStatusCode.BadRequest, HttpStatusCode.NotFound);
        (await ReadAsync()).Items.Should().ContainSingle();
        await AssertNoFinancialEffectsAsync();
    }

    [Fact]
    public async Task Stale_review_type_mismatch_empty_basket_and_disabled_channel_are_refused()
    {
        var basket = await CheckoutBasketAsync();
        var review = Review(basket);
        (await QuoteResponseAsync(review with { Type = OrderType.Delivery })).StatusCode.Should().Be(HttpStatusCode.Conflict);
        await BasketAsync(await MutateAsync(basket, Update(basket)));
        (await QuoteResponseAsync(review)).StatusCode.Should().Be(HttpStatusCode.Conflict);
        var current = await ReadAsync();
        await EnableAsync(OrderType.Takeaway, false);
        (await QuoteResponseAsync(Review(current))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        await EnableAsync(OrderType.Takeaway, true);
        await BasketAsync(await MutateAsync(current, Update(current) with
        {
            Action = RestaurantSystem.Api.Features.Basket.Dtos.Requests.CustomerBasketItemAction.Remove,
            Quantity = null,
            SpecialInstructions = null
        }));
        (await QuoteResponseAsync(Review(await ReadAsync()))).StatusCode.Should().Be(HttpStatusCode.Conflict);
        await AssertNoFinancialEffectsAsync();
    }

    [Fact]
    public async Task Guest_cannot_request_a_points_discount_and_foreign_sessions_cannot_quote_the_basket()
    {
        var basket = await CheckoutBasketAsync();
        (await QuoteResponseAsync(Review(basket) with { PointsToRedeem = 1 })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await QuoteResponseAsync(Review(basket), Guid.NewGuid().ToString("D"))).StatusCode.Should().Be(HttpStatusCode.Conflict);
        await AssertNoFinancialEffectsAsync();
    }
}
