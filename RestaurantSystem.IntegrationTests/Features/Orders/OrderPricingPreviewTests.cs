using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.FidelityPoints.Services;
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
public sealed class OrderPricingPreviewTests(DatabaseFixture fixture) : IAsyncLifetime
{
    public Task InitializeAsync() => fixture.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Repeated_reviews_preserve_discount_usage_and_eligibility_even_after_a_later_save()
    {
        await using var context = fixture.CreateContext();
        var (userId, discountId) = await SeedDiscountAsync(context);
        var pricing = CreatePricing(context, userId);

        for (var attempt = 0; attempt < 2; attempt++)
        {
            var quote = Quote(3m);
            await pricing.PreviewAsync(quote, 100m, Delivery(), userId, CancellationToken.None);
            AssertQuote(quote, discountId);
        }

        context.ChangeTracker.HasChanges().Should().BeFalse();
        await context.SaveChangesAsync();
        await using var readback = fixture.CreateContext();
        var discount = await readback.CustomerDiscountRules.AsNoTracking().SingleAsync();
        discount.UsageCount.Should().Be(0);
        discount.IsActive.Should().BeTrue();
        (await readback.Orders.AnyAsync()).Should().BeFalse();
        (await readback.FidelityPointsTransactions.AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task Acceptance_still_consumes_the_same_individual_discount_once()
    {
        await using var context = fixture.CreateContext();
        var (userId, discountId) = await SeedDiscountAsync(context);
        var pricing = CreatePricing(context, userId);
        await pricing.PreviewAsync(Quote(3m), 100m, Delivery(), userId, CancellationToken.None);

        var accepted = Quote(3m);
        await pricing.ApplyAsync(accepted, 100m, Delivery(), userId, CancellationToken.None);
        AssertQuote(accepted, discountId);

        await using var readback = fixture.CreateContext();
        var discount = await readback.CustomerDiscountRules.AsNoTracking().SingleAsync();
        discount.UsageCount.Should().Be(1);
        discount.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task Guest_review_and_acceptance_keep_identical_undiscounted_money_without_creating_rows()
    {
        await using var context = fixture.CreateContext();
        var pricing = CreatePricing(context, Guid.NewGuid());
        var command = new CreateOrderCommand { Type = OrderType.Takeaway };
        var quote = Quote(2.5m);
        var accepted = Quote(2.5m);

        await pricing.PreviewAsync(quote, 12.99m, command, null, CancellationToken.None);
        await pricing.ApplyAsync(accepted, 12.99m, command, null, CancellationToken.None);

        quote.Total.Should().Be(15.49m);
        accepted.Total.Should().Be(15.49m);
        quote.DeliveryFee.Should().Be(0m);
        quote.CustomerDiscountAmount.Should().Be(0m);
        context.ChangeTracker.HasChanges().Should().BeFalse();
    }

    private static Order Quote(decimal tip) => new() { Tip = tip, CreatedBy = "quote-fixture" };

    private static CreateOrderCommand Delivery() => new() { Type = OrderType.Delivery };

    private static void AssertQuote(Order order, Guid discountId)
    {
        // Independently expected: 100 food + 4 delivery - 20 discount + 3 chosen tip.
        order.SubTotal.Should().Be(100m);
        order.Tax.Should().Be(0m);
        order.DeliveryFee.Should().Be(4m);
        order.CustomerDiscountAmount.Should().Be(20m);
        order.CustomerDiscountRuleId.Should().Be(discountId);
        order.Total.Should().Be(87m);
    }

    private static OrderPricingService CreatePricing(ApplicationDbContext context, Guid userId)
    {
        var currentUser = new Mock<ICurrentUserService>();
        currentUser.Setup(user => user.UserId).Returns(userId);
        currentUser.Setup(user => user.GetAuditIdentifier()).Returns(userId.ToString());
        return new OrderPricingService(context, new CustomerDiscountService(context, currentUser.Object),
            Options.Create(new OrderSettings { DeliveryFee = 4m }), NullLogger<OrderPricingService>.Instance);
    }

    private static async Task<(Guid UserId, Guid DiscountId)> SeedDiscountAsync(ApplicationDbContext context)
    {
        var userId = Guid.NewGuid();
        await TestUserSeeder.SeedUserAsync(context, userId);
        var discount = new CustomerDiscountRule
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Name = "Limited review discount",
            DiscountType = DiscountType.Percentage,
            DiscountValue = 20m,
            MaxUsageCount = 1,
            UsageCount = 0,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "quote-fixture"
        };
        context.CustomerDiscountRules.Add(discount);
        await context.SaveChangesAsync();
        return (userId, discount.Id);
    }
}
