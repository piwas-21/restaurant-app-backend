using System.Net;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using RestaurantSystem.Api.Features.Orders.Commands.CreateOrderCommand;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Api.Settings;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Common;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

[Collection("Database Lane 4")]
public sealed class CustomerCheckoutAcceptanceFinancialTests(DatabaseFixture fixture) : CustomerCheckoutAcceptanceFixture(fixture)
{
    private string _fault = string.Empty;
    protected override void ConfigureTestServices(IServiceCollection services)
    {
        base.ConfigureTestServices(services);
        services.PostConfigure<CustomerMobileSettings>(settings => settings.EnabledCapabilities = ["ordering", "loyalty"]);
        services.AddScoped<OrderPricingService>();
        services.RemoveAll<IOrderPricingService>();
        services.AddScoped<IOrderPricingService>(provider => new AdversarialPricing(
            provider.GetRequiredService<OrderPricingService>(), provider.GetRequiredService<ApplicationDbContext>(), () => _fault));
    }

    protected override async Task SeedTestData()
    {
        await base.SeedTestData();
        await using var context = DatabaseFixture.CreateContext();
        var owner = Guid.Parse(TestAuthHandler.UserId);
        context.CustomerDiscountRules.Add(new()
        {
            UserId = owner,
            Name = "One-use native discount",
            DiscountType = DiscountType.Percentage,
            DiscountValue = 20m,
            MaxUsageCount = 1,
            IsActive = true,
            CreatedBy = "native-financial-fixture"
        });
        context.FidelityPointBalances.Add(new()
        {
            UserId = owner,
            CurrentPoints = 500,
            TotalEarnedPoints = 500,
            CreatedBy = "native-financial-fixture"
        });
        await context.SaveChangesAsync();
        AuthenticateAsUser();
    }

    [Fact]
    public async Task Accepted_discount_and_points_are_consumed_once_and_immutable_money_matches_review()
    {
        var intent = await IntentAsync();
        intent = intent with { Review = intent.Review with { PointsToRedeem = 100 } };
        var quote = await QuoteAsync(intent.Review);
        intent = intent with { ExpectedQuoteFingerprint = quote.QuoteFingerprint };
        var submission = await PreparedAsync(intent);
        var first = await OrderResolutionAsync(await SubmitAsync(submission));
        (await OrderResolutionAsync(await SubmitAsync(submission))).Should().Be(first);
        await using var read = DatabaseFixture.CreateContext();
        var order = await read.Orders.SingleAsync();
        order.Total.Should().Be(14m); // ceil(14 - 2.8) - 1 point credit + 3 tip.
        order.CustomerDiscountAmount.Should().Be(2.8m);
        order.FidelityPointsRedeemed.Should().Be(100);
        order.FidelityPointsDiscount.Should().Be(1m);
        order.TotalPaid.Should().Be(0m);
        (await read.OrderPayments.SingleAsync()).Amount.Should().Be(14m);
        (await read.CustomerDiscountRules.SingleAsync()).UsageCount.Should().Be(1);
        (await read.CustomerDiscountRules.SingleAsync()).IsActive.Should().BeFalse();
        (await read.FidelityPointBalances.SingleAsync()).CurrentPoints.Should().Be(400);
        (await read.FidelityPointsTransactions.CountAsync()).Should().Be(1);
        var snapshot = await read.Set<OrderBillingSnapshot>().SingleAsync();
        snapshot.TotalMinor.Should().Be(1400);
        snapshot.RedemptionDiscountMinor.Should().Be(100);
        snapshot.RedeemedPoints.Should().Be(100);
        snapshot.CustomerDiscountMinor.Should().Be(280);
    }

    [Theory]
    [InlineData("price-drift")]
    [InlineData("points-race")]
    public async Task Financial_change_after_review_rolls_back_order_snapshot_discount_points_and_basket_clear(string fault)
    {
        var intent = await IntentAsync();
        intent = intent with { Review = intent.Review with { PointsToRedeem = 100 } };
        intent = intent with { ExpectedQuoteFingerprint = (await QuoteAsync(intent.Review)).QuoteFingerprint };
        var submission = await PreparedAsync(intent);
        _fault = fault;
        (await SubmitAsync(submission)).StatusCode.Should().BeOneOf(HttpStatusCode.BadRequest, HttpStatusCode.Conflict);
        await AssertNoFinancialEffectsAsync();
        await using var read = DatabaseFixture.CreateContext();
        (await read.Set<OrderBillingSnapshot>().CountAsync()).Should().Be(0);
        (await read.Set<CustomerOrderOperation>().CountAsync()).Should().Be(0);
        (await read.FidelityPointBalances.SingleAsync()).CurrentPoints.Should().Be(500);
        (await read.CustomerDiscountRules.SingleAsync()).IsActive.Should().BeTrue();
        (await ReadAsync()).Items.Should().ContainSingle();
    }

    private sealed class AdversarialPricing(OrderPricingService real, ApplicationDbContext context, Func<string> fault)
        : IOrderPricingService
    {
        public async Task ApplyAsync(Order order, decimal itemsTotal, CreateOrderCommand command, Guid? userId,
            CancellationToken cancellationToken)
        {
            await real.ApplyAsync(order, itemsTotal, command, userId, cancellationToken);
            if (fault() == "price-drift")
            {
                order.DeliveryFee += 1m;
                real.RecalculateTotal(order);
            }
            if (fault() == "points-race")
                await context.FidelityPointBalances.Where(value => value.UserId == userId)
                    .ExecuteUpdateAsync(update => update.SetProperty(value => value.CurrentPoints, 0), cancellationToken);
        }
        public Task PreviewAsync(Order order, decimal itemsTotal, CreateOrderCommand command, Guid? userId,
            CancellationToken cancellationToken) => real.PreviewAsync(order, itemsTotal, command, userId, cancellationToken);
        public void RecalculateTotal(Order order) => real.RecalculateTotal(order);
    }
}
