using System.Net;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Features.Basket.Dtos;
using RestaurantSystem.Api.Settings;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Basket;

[Collection("Database Lane 2")]
public sealed class CustomerBasketCancellationWithdrawalTests(DatabaseFixture fixture) : CustomerBasketCancellationFixture(fixture)
{
    [Fact]
    public async Task Withdrawn_rollout_allows_sealing_but_cannot_enable_late_execution()
    {
        var request = AddRequest();
        var settings = Factory.Services.GetRequiredService<IOptions<CustomerMobileSettings>>().Value;
        settings.EnabledCapabilities = [];
        (await ResolutionAsync(await CancelAddAsync(request))).Outcome.Should().Be(CustomerBasketOperationOutcome.Cancelled);
        settings.EnabledCapabilities = ["ordering"];
        (await SendAsync("/api/customer-mobile/basket/items", request)).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await BasketAsync(await SendAsync("/api/customer-mobile/basket/items", AddRequest()))).TotalItems.Should().Be(2);
    }

    [Fact]
    public async Task Changed_current_validation_does_not_block_sealing_or_proving_an_accepted_original()
    {
        var accepted = AddRequest();
        var basket = await BasketAsync(await SendAsync("/api/customer-mobile/basket/items", accepted));
        var neverSent = AddRequest() with { Quantity = 3 };
        var settings = Factory.Services.GetRequiredService<IOptions<BasketSettings>>().Value;
        settings.MaxQuantityPerItem = 1;
        Factory.Services.GetRequiredService<IOptions<CustomerMobileSettings>>().Value.EnabledCapabilities = [];
        var applied = await ResolutionAsync(await CancelAddAsync(accepted));
        applied.Outcome.Should().Be(CustomerBasketOperationOutcome.Applied);
        applied.Basket!.Id.Should().Be(basket.Id);
        applied.Basket.TotalItems.Should().Be(2);
        (await ResolutionAsync(await CancelAddAsync(neverSent))).Outcome.Should().Be(CustomerBasketOperationOutcome.Cancelled);
        (await ReceiptsAsync()).Count.Should().Be(2);
    }
}
