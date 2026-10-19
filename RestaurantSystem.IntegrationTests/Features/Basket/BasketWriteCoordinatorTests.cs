using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RestaurantSystem.Api.Features.Basket.Interfaces;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Common;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Basket;

[Collection("Database Lane 2")]
public sealed class BasketWriteCoordinatorTests(DatabaseFixture fixture) : CustomerBasketMutationFixture(fixture)
{
    [Fact]
    public async Task An_aborted_basket_cannot_leak_through_a_later_unrelated_save_in_the_same_login_scope()
    {
        var initial = await AddAsync();
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var writes = scope.ServiceProvider.GetRequiredService<IBasketWriteCoordinator>();
        var user = await context.Users.SingleAsync(row => row.Id == Guid.Parse(TestAuthHandler.UserId));
        Func<Task> abort = () => writes.RunAsync<bool>(Session, null, async () =>
        {
            var basket = await context.Baskets.Include(row => row.Items).SingleAsync(row => row.Id == initial.Id);
            basket.Items.Single().Quantity = 9;
            basket.SubTotal = 63m;
            throw new IOException("Interrupted basket mutation before save.");
        });
        await abort.Should().ThrowAsync<IOException>();
        user.FirstName = "Unrelated successful account edit";
        await context.SaveChangesAsync();
        using var verify = Factory.Services.CreateScope();
        var fresh = verify.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var persisted = await fresh.Baskets.Include(row => row.Items).SingleAsync(row => row.Id == initial.Id);
        persisted.Items.Single().Quantity.Should().Be(2);
        persisted.SubTotal.Should().Be(14m);
        (await fresh.Users.SingleAsync(row => row.Id == user.Id)).FirstName.Should().Be(user.FirstName);
    }

    [Fact]
    public async Task Existing_outer_transactions_are_not_committed_by_a_nested_basket_service_write()
    {
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var service = scope.ServiceProvider.GetRequiredService<IBasketService>();
        await using (await context.Database.BeginTransactionAsync())
        {
            await service.AddItemToBasketAsync(Session, null, new() { ProductId = ProductId, Quantity = 2 });
            using var observer = Factory.Services.CreateScope();
            (await observer.ServiceProvider.GetRequiredService<ApplicationDbContext>().Baskets.CountAsync()).Should().Be(0);
        }
        using var final = Factory.Services.CreateScope();
        (await final.ServiceProvider.GetRequiredService<ApplicationDbContext>().Baskets.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Standalone_recalculation_uses_the_actual_owner_and_missing_baskets_are_a_noop()
    {
        var basket = await AddAsync();
        using (var scope = Factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var tracked = await context.Baskets.SingleAsync(row => row.Id == basket.Id);
            tracked.SubTotal = 987m;
            tracked.Total = 999m;
            await context.SaveChangesAsync();
            var service = scope.ServiceProvider.GetRequiredService<IBasketService>();
            await service.RecalculateBasketTotalsAsync(Guid.NewGuid());
            await service.RecalculateBasketTotalsAsync(basket.Id);
        }
        using var final = Factory.Services.CreateScope();
        var persisted = await final.ServiceProvider.GetRequiredService<ApplicationDbContext>().Baskets.SingleAsync(row => row.Id == basket.Id);
        persisted.SubTotal.Should().Be(14m);
        persisted.Total.Should().Be(basket.Total);
    }
}
