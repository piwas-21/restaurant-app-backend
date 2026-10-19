using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Basket.Dtos;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Features.Basket;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

public abstract class CustomerCheckoutFixture(DatabaseFixture fixture) : CustomerBasketChannelFixture(fixture)
{
    protected const string QuotePath = "/api/customer-mobile/checkout/quote";

    protected async Task<BasketDto> CheckoutBasketAsync(OrderType type = OrderType.Takeaway) =>
        await BasketAsync(await SwitchAsync(Channel(await AddAsync(), type)));

    protected static CustomerCheckoutReviewDto Review(BasketDto basket, decimal tip = 3m) => new()
    {
        SchemaVersion = 1,
        ExpectedBasketId = basket.Id,
        ExpectedPurchaseFingerprint = basket.PurchaseFingerprint,
        Type = basket.OrderType!.Value,
        PointsToRedeem = 0,
        Tip = tip
    };

    protected Task<HttpResponseMessage> QuoteResponseAsync(CustomerCheckoutReviewDto review, string? session = null) =>
        SendAsync(QuotePath, review, session);

    protected async Task<CustomerCheckoutQuoteDto> QuoteAsync(CustomerCheckoutReviewDto review)
    {
        var response = await QuoteResponseAsync(review);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        return (await response.Content.ReadFromJsonAsync<ApiResponse<CustomerCheckoutQuoteDto>>(JsonOptions))!.Data!;
    }

    protected async Task AssertNoFinancialEffectsAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        (await context.Orders.CountAsync()).Should().Be(0);
        (await context.OrderItems.CountAsync()).Should().Be(0);
        (await context.OrderPayments.CountAsync()).Should().Be(0);
        (await context.FidelityPointsTransactions.CountAsync()).Should().Be(0);
        (await context.CustomerDiscountRules.AnyAsync(rule => rule.UsageCount != 0)).Should().BeFalse();
    }
}
