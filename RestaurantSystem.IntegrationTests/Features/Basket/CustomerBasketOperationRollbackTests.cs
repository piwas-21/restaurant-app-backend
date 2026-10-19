using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Basket.Dtos;
using RestaurantSystem.Api.Features.Basket.Dtos.Requests;
using RestaurantSystem.Api.Features.Basket.Interfaces;
using RestaurantSystem.Api.Features.Basket.Services;
using RestaurantSystem.Api.Settings;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Basket;

[Collection("Database Lane 2")]
public sealed class CustomerBasketOperationRollbackTests(DatabaseFixture databaseFixture) : IntegrationTestBase(databaseFixture)
{
    private int _failuresRemaining = 1;

    protected override void ConfigureTestServices(IServiceCollection services)
    {
        services.PostConfigure<CustomerMobileSettings>(settings =>
        {
            settings.Enabled = true; settings.TenantId = Guid.NewGuid();
            settings.Lifecycle = "active"; settings.EnabledCapabilities = ["ordering"];
        });
        services.RemoveAll<IBasketService>();
        services.AddScoped(provider =>
        {
            var real = ActivatorUtilities.CreateInstance<BasketService>(provider);
            var wrapper = new Mock<IBasketService>(MockBehavior.Strict);
            wrapper.Setup(service => service.AddItemToBasketAsync(It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<AddToBasketDto>()))
                .Returns(async (string session, Guid? userId, AddToBasketDto request) =>
                {
                    var result = await real.AddItemToBasketAsync(session, userId, request);
                    if (Interlocked.Exchange(ref _failuresRemaining, 0) == 1)
                        throw new IOException("Injected interruption after basket persistence and before receipt persistence.");
                    return result;
                });
            wrapper.Setup(service => service.GetBasketAsync(It.IsAny<string>(), It.IsAny<Guid?>()))
                .Returns((string session, Guid? userId) => real.GetBasketAsync(session, userId));
            return wrapper.Object;
        });
    }

    [Fact]
    public async Task An_interruption_after_all_basket_saves_rolls_back_lines_and_receipt_before_retry()
    {
        AuthenticateAsAnonymous();
        Guid productId;
        using (var scope = Factory.Services.CreateScope())
            productId = await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>()
                .Products.Where(product => product.Name == "Test Cola").Select(product => product.Id).SingleAsync();
        Client.DefaultRequestHeaders.Add("X-Session-Id", Guid.NewGuid().ToString("D"));
        var request = new AddToBasketDto { ProductId = productId, Quantity = 2, OperationId = Guid.NewGuid() };
        (await PostAsJsonAsync("/api/basket/items", request)).StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        using (var verify = Factory.Services.CreateScope())
        {
            var context = verify.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            (await context.Baskets.CountAsync()).Should().Be(0);
            (await context.BasketItems.CountAsync()).Should().Be(0);
            (await context.Set<CustomerBasketOperation>().CountAsync()).Should().Be(0);
        }
        var retry = await PostAsJsonAsync("/api/basket/items", request);
        retry.StatusCode.Should().Be(HttpStatusCode.OK, await retry.Content.ReadAsStringAsync());
        var envelope = await retry.Content.ReadFromJsonAsync<ApiResponse<BasketDto>>(JsonOptions);
        envelope!.Success.Should().BeTrue();
        envelope.Data!.Items.Single().Quantity.Should().Be(2);
        using var final = Factory.Services.CreateScope();
        (await final.ServiceProvider.GetRequiredService<ApplicationDbContext>().Set<CustomerBasketOperation>().CountAsync()).Should().Be(1);
    }
}
