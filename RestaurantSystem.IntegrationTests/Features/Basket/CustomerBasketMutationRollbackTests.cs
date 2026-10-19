using System.Net;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;
using RestaurantSystem.Api.Features.Basket.Dtos.Requests;
using RestaurantSystem.Api.Features.Basket.Interfaces;
using RestaurantSystem.Api.Features.Basket.Services;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Basket;

[Collection("Database Lane 2")]
public sealed class CustomerBasketMutationRollbackTests(DatabaseFixture fixture) : CustomerBasketMutationFixture(fixture)
{
    private int _failuresRemaining = 1;
    protected override void ConfigureTestServices(IServiceCollection services)
    {
        base.ConfigureTestServices(services);
        services.RemoveAll<IBasketService>();
        services.AddScoped(provider =>
        {
            var real = ActivatorUtilities.CreateInstance<BasketService>(provider);
            var wrapper = new Mock<IBasketService>(MockBehavior.Strict);
            wrapper.Setup(service => service.AddItemToBasketAsync(It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<AddToBasketDto>()))
                .Returns((string session, Guid? userId, AddToBasketDto request) => real.AddItemToBasketAsync(session, userId, request));
            wrapper.Setup(service => service.GetBasketAsync(It.IsAny<string>(), It.IsAny<Guid?>()))
                .Returns((string session, Guid? userId) => real.GetBasketAsync(session, userId));
            wrapper.Setup(service => service.UpdateBasketItemAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<UpdateBasketItemDto>()))
                .Returns(async (string session, Guid item, UpdateBasketItemDto request) =>
                {
                    var result = await real.UpdateBasketItemAsync(session, item, request);
                    FailAfterPersistence();
                    return result;
                });
            wrapper.Setup(service => service.RemoveItemFromBasketAsync(It.IsAny<string>(), It.IsAny<Guid>()))
                .Returns(async (string session, Guid item) =>
                {
                    var result = await real.RemoveItemFromBasketAsync(session, item);
                    FailAfterPersistence();
                    return result;
                });
            return wrapper.Object;
        });
    }

    private void FailAfterPersistence()
    {
        if (Interlocked.Exchange(ref _failuresRemaining, 0) == 1)
            throw new IOException("Injected interruption after item persistence and before receipt persistence.");
    }

    [Theory]
    [InlineData(CustomerBasketItemAction.Update)]
    [InlineData(CustomerBasketItemAction.Remove)]
    public async Task Interrupted_mutations_roll_back_the_whole_effect_and_allow_exact_recovery(CustomerBasketItemAction action)
    {
        var basket = await AddAsync();
        var request = Update(basket);
        if (action == CustomerBasketItemAction.Remove)
            request = request with { Action = action, Quantity = null, SpecialInstructions = null };
        (await MutateAsync(basket, request)).StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        using (var scope = Factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var persisted = await context.Baskets.Include(row => row.Items).SingleAsync(row => row.Id == basket.Id);
            persisted.Items.Single().Quantity.Should().Be(2);
            persisted.Items.Single().SpecialInstructions.Should().BeNull();
            persisted.SubTotal.Should().Be(14m);
        }
        (await ReceiptsAsync()).Should().BeEmpty();
        var recovered = await BasketAsync(await MutateAsync(basket, request));
        if (action == CustomerBasketItemAction.Remove) recovered.Items.Should().BeEmpty();
        else recovered.SubTotal.Should().Be(21m);
        (await ReceiptsAsync()).Should().ContainSingle();
    }
}
