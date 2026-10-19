using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.FidelityPoints.Services;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Common;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.FidelityPoints;

[Collection("Database Lane 4")]
public sealed class CustomerDiscountConsumptionTests(DatabaseFixture fixture) : IAsyncLifetime
{
    private readonly Guid _userId = Guid.NewGuid();
    public async Task InitializeAsync()
    {
        await fixture.ResetDatabaseAsync();
        await using var context = fixture.CreateContext();
        await TestUserSeeder.SeedUserAsync(context, _userId);
    }
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Separate_stale_contexts_cannot_consume_the_same_final_use()
    {
        var id = await SeedAsync(1);
        await using var first = fixture.CreateContext();
        await using var second = fixture.CreateContext();
        // Preload both contexts independently; EF identity resolution must not supply authority.
        await first.CustomerDiscountRules.SingleAsync(rule => rule.Id == id);
        await second.CustomerDiscountRules.SingleAsync(rule => rule.Id == id);
        var results = await Task.WhenAll(TryConsumeAsync(first, id), TryConsumeAsync(second, id));
        results.Count(accepted => accepted).Should().Be(1);
        await using var readback = fixture.CreateContext();
        var saved = await readback.CustomerDiscountRules.SingleAsync(rule => rule.Id == id);
        saved.UsageCount.Should().Be(1);
        saved.IsActive.Should().BeFalse();
        first.ChangeTracker.HasChanges().Should().BeFalse();
        second.ChangeTracker.HasChanges().Should().BeFalse();
        await first.SaveChangesAsync();
        await second.SaveChangesAsync();
        await readback.Entry(saved).ReloadAsync();
        saved.UsageCount.Should().Be(1);
    }

    [Fact]
    public async Task Concurrent_unlimited_consumers_preserve_every_increment_and_return_distinct_receipts()
    {
        const int consumers = 8;
        var id = await SeedAsync(null);
        var contexts = Enumerable.Range(0, consumers).Select(_ => fixture.CreateContext()).ToArray();
        try
        {
            foreach (var context in contexts)
                await context.CustomerDiscountRules.SingleAsync(rule => rule.Id == id);
            var results = await Task.WhenAll(contexts.Select(context => Service(context).ApplyDiscountAsync(id)));
            results.Select(rule => rule.UsageCount).Order().Should().Equal(Enumerable.Range(1, consumers));
            await using var readback = fixture.CreateContext();
            var saved = await readback.CustomerDiscountRules.SingleAsync(rule => rule.Id == id);
            saved.UsageCount.Should().Be(consumers);
            saved.IsActive.Should().BeTrue();
        }
        finally
        {
            foreach (var context in contexts) await context.DisposeAsync();
        }
    }

    [Theory]
    [InlineData("inactive")]
    [InlineData("expired")]
    [InlineData("future")]
    [InlineData("exhausted")]
    [InlineData("overflow")]
    [InlineData("negative")]
    public async Task Ineligible_rules_refuse_without_changing_usage(string reason)
    {
        var id = await SeedAsync(null, rule =>
        {
            if (reason == "inactive") rule.IsActive = false;
            if (reason == "expired") rule.ValidUntil = DateTime.UtcNow.AddDays(-1);
            if (reason == "future") rule.ValidFrom = DateTime.UtcNow.AddDays(1);
            if (reason == "exhausted") { rule.MaxUsageCount = 1; rule.UsageCount = 1; }
            if (reason == "overflow") rule.UsageCount = int.MaxValue;
            if (reason == "negative") rule.UsageCount = -1;
        });
        await using var context = fixture.CreateContext();
        var before = await context.CustomerDiscountRules.AsNoTracking().SingleAsync(rule => rule.Id == id);
        var act = () => Service(context).ApplyDiscountAsync(id);
        await act.Should().ThrowAsync<ConflictException>();
        context.ChangeTracker.HasChanges().Should().BeFalse();
        var after = await context.CustomerDiscountRules.AsNoTracking().SingleAsync(rule => rule.Id == id);
        after.UsageCount.Should().Be(before.UsageCount);
        after.IsActive.Should().Be(before.IsActive);
        after.UpdatedAt.Should().Be(before.UpdatedAt);
    }

    [Fact]
    public async Task Consumption_rolls_back_with_the_callers_order_transaction()
    {
        var id = await SeedAsync(1);
        await using var context = fixture.CreateContext();
        await using (var transaction = await context.Database.BeginTransactionAsync())
        {
            var result = await Service(context).ApplyDiscountAsync(id);
            result.UsageCount.Should().Be(1);
            result.IsActive.Should().BeFalse();
            await transaction.RollbackAsync();
        }
        await context.SaveChangesAsync();
        await using var readback = fixture.CreateContext();
        var saved = await readback.CustomerDiscountRules.SingleAsync(rule => rule.Id == id);
        saved.UsageCount.Should().Be(0);
        saved.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task Missing_rule_retains_not_found_behavior()
    {
        await using var context = fixture.CreateContext();
        var act = () => Service(context).ApplyDiscountAsync(Guid.NewGuid());
        await act.Should().ThrowAsync<NotFoundException>();
    }

    private async Task<Guid> SeedAsync(int? limit, Action<CustomerDiscountRule>? configure = null)
    {
        await using var context = fixture.CreateContext();
        var rule = new CustomerDiscountRule
        {
            UserId = _userId,
            Name = "Limited checkout discount",
            DiscountType = DiscountType.Percentage,
            DiscountValue = 20m,
            MaxUsageCount = limit,
            UsageCount = 0,
            IsActive = true,
            CreatedBy = "discount-consumption"
        };
        configure?.Invoke(rule);
        context.CustomerDiscountRules.Add(rule);
        await context.SaveChangesAsync();
        return rule.Id;
    }

    private static CustomerDiscountService Service(ApplicationDbContext context)
    {
        var user = new Mock<ICurrentUserService>();
        user.Setup(value => value.GetAuditIdentifier()).Returns("discount-consumption");
        return new(context, user.Object);
    }
    private static async Task<bool> TryConsumeAsync(ApplicationDbContext context, Guid id)
    {
        try { await Service(context).ApplyDiscountAsync(id); return true; }
        catch (ConflictException) { return false; }
    }
}
