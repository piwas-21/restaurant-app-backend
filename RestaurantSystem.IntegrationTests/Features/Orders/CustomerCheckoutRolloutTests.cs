using System.Net;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Settings;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

[Collection("Database Lane 4")]
public sealed class CustomerCheckoutRolloutTests(DatabaseFixture fixture) : CustomerCheckoutFixture(fixture)
{
    protected override void ConfigureTestServices(IServiceCollection services)
    {
        base.ConfigureTestServices(services);
        services.PostConfigure<CustomerMobileSettings>(settings => settings.Enabled = false);
    }

    [Fact]
    public async Task Native_quotes_remain_unavailable_without_rollout_while_legacy_baskets_continue_working()
    {
        var basket = await AddAsync();
        basket.Items.Should().ContainSingle();
        var review = new CustomerCheckoutReviewDto
        {
            SchemaVersion = 1,
            ExpectedBasketId = basket.Id,
            ExpectedPurchaseFingerprint = basket.PurchaseFingerprint,
            Type = OrderType.Takeaway,
            PointsToRedeem = 0,
            Tip = 0
        };
        (await QuoteResponseAsync(review)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        await AssertNoFinancialEffectsAsync();
    }
}
