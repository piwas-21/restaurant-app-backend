using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;
using RestaurantSystem.Api.Features.Basket.Dtos;
using RestaurantSystem.Api.Features.Basket.Dtos.Requests;
using RestaurantSystem.Api.Features.Basket.Interfaces;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Common;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Basket;

[Collection("Database Lane 2")]
public sealed class CustomerBasketWriteRaceTests(DatabaseFixture fixture) : CustomerBasketMutationFixture(fixture)
{
    private readonly TaskCompletionSource _reviewed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _legacyEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _pauseRead;
    private static readonly TimeSpan GateTimeout = TimeSpan.FromSeconds(20);

    protected override void ConfigureTestServices(IServiceCollection services)
    {
        base.ConfigureTestServices(services);
        // Wrap the actual production registration, so restoring the old undecorated registration
        // makes this regression fail instead of quietly testing a special test-only lock.
        var basketType = services.Last(service => service.ServiceType == typeof(IBasketService)).ImplementationType!;
        var channelType = services.Last(service => service.ServiceType == typeof(IBasketChannelService)).ImplementationType!;
        services.RemoveAll<IBasketService>();
        services.AddScoped(provider => BasketProxy((IBasketService)ActivatorUtilities.CreateInstance(provider, basketType)));
        services.RemoveAll<IBasketChannelService>();
        services.AddScoped(provider => ChannelProxy((IBasketChannelService)ActivatorUtilities.CreateInstance(provider, channelType)));
    }

    private IBasketService BasketProxy(IBasketService real)
    {
        var mock = new Mock<IBasketService>(MockBehavior.Strict);
        mock.Setup(service => service.AddItemToBasketAsync(It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<AddToBasketDto>()))
            .Returns((string session, Guid? user, AddToBasketDto input) => real.AddItemToBasketAsync(session, user, input));
        mock.Setup(service => service.GetBasketAsync(It.IsAny<string>(), It.IsAny<Guid?>()))
            .Returns(async (string session, Guid? user) =>
            {
                var result = await real.GetBasketAsync(session, user);
                if (Interlocked.Exchange(ref _pauseRead, 0) == 1)
                {
                    _reviewed.TrySetResult();
                    await _release.Task.WaitAsync(GateTimeout);
                }
                return result;
            });
        mock.Setup(service => service.UpdateBasketItemAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<UpdateBasketItemDto>()))
            .Returns((string session, Guid item, UpdateBasketItemDto input) =>
            {
                _legacyEntered.TrySetResult();
                return real.UpdateBasketItemAsync(session, item, input);
            });
        mock.Setup(service => service.MergeAnonymousBasketAsync(It.IsAny<string>(), It.IsAny<Guid>()))
            .Returns((string session, Guid user) =>
            {
                _legacyEntered.TrySetResult();
                return real.MergeAnonymousBasketAsync(session, user);
            });
        return mock.Object;
    }

    private IBasketChannelService ChannelProxy(IBasketChannelService real)
    {
        var mock = new Mock<IBasketChannelService>(MockBehavior.Strict);
        mock.Setup(service => service.SetOrderTypeAsync(It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<OrderType>(),
            It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Returns((string session, Guid? user, OrderType type, bool remove, CancellationToken token) =>
            {
                _legacyEntered.TrySetResult();
                return real.SetOrderTypeAsync(session, user, type, remove, token);
            });
        return mock.Object;
    }

    [Theory]
    [InlineData("quantity")]
    [InlineData("channel")]
    [InlineData("merge")]
    public async Task Legacy_writers_cannot_commit_between_native_review_and_application(string writer)
    {
        var basket = await AddAsync();
        _pauseRead = 1;
        var native = MutateAsync(basket, Update(basket));
        Task? legacy = null;
        try
        {
            await _reviewed.Task.WaitAsync(GateTimeout);
            legacy = WriteLegacyAsync(writer, basket);
            await _legacyEntered.Task.WaitAsync(GateTimeout);
            var observation = Task.Delay(TimeSpan.FromSeconds(2));
            (await Task.WhenAny(legacy, observation)).Should().Be(observation,
                "an entered legacy writer must wait for the native validation/application boundary");
            using (var scope = Factory.Services.CreateScope())
                (await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().BasketItems
                    .SingleAsync(item => item.Id == basket.Items[0].Id)).Quantity.Should().Be(2);
            _release.TrySetResult();
            (await BasketAsync(await native)).Items.Single().Quantity.Should().Be(3);
            await legacy.WaitAsync(GateTimeout);
            using var final = Factory.Services.CreateScope();
            var persisted = await final.ServiceProvider.GetRequiredService<ApplicationDbContext>().Baskets
                .Include(row => row.Items).SingleAsync(row => row.Id == basket.Id);
            persisted.Items.Single().Quantity.Should().Be(writer == "quantity" ? 5 : 3);
            persisted.SubTotal.Should().Be(writer == "quantity" ? 35m : 21m);
            if (writer == "channel") persisted.OrderType.Should().Be(OrderType.Takeaway);
            if (writer == "merge") persisted.UserId.Should().Be(Guid.Parse(TestAuthHandler.UserId));
            (await ReceiptsAsync()).Should().ContainSingle();
        }
        finally
        {
            _release.TrySetResult();
            await native;
            if (legacy is not null) await legacy;
        }
    }

    private async Task WriteLegacyAsync(string writer, BasketDto basket)
    {
        if (writer == "merge")
        {
            using var scope = Factory.Services.CreateScope();
            await scope.ServiceProvider.GetRequiredService<IBasketService>()
                .MergeAnonymousBasketAsync(Session, Guid.Parse(TestAuthHandler.UserId));
            return;
        }
        var path = writer == "channel" ? "/api/Basket/order-type" : $"/api/Basket/items/{basket.Items[0].Id}";
        object body = writer == "channel"
            ? new { orderType = "Takeaway", removeConflicts = false }
            : new UpdateBasketItemDto { Quantity = 5, SpecialInstructions = "Web note" };
        using var request = new HttpRequestMessage(HttpMethod.Put, new Uri(path, UriKind.Relative))
        {
            Content = JsonContent.Create(body, options: JsonOptions)
        };
        request.Headers.Add("X-Session-Id", Session);
        var response = await Client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }
}
