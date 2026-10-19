using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Basket.Dtos;
using RestaurantSystem.Api.Features.Basket.Dtos.Requests;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Basket;

public abstract class CustomerBasketCancellationFixture(DatabaseFixture fixture) : CustomerBasketMutationFixture(fixture)
{
    protected AddToBasketDto AddRequest() => new() { ProductId = ProductId, Quantity = 2, OperationId = Guid.NewGuid() };
    protected Task<HttpResponseMessage> CancelAddAsync(AddToBasketDto request, string? session = null) =>
        SendAsync("/api/customer-mobile/basket/items/cancel", request, session);
    protected Task<HttpResponseMessage> CancelMutationAsync(BasketDto basket, CustomerBasketItemMutationDto request,
        string? session = null, Guid? itemId = null) =>
        SendAsync($"/api/customer-mobile/basket/items/{itemId ?? basket.Items[0].Id}/operations/cancel", request, session);

    protected static async Task<CustomerBasketOperationResolutionDto> ResolutionAsync(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var result = await response.Content.ReadFromJsonAsync<ApiResponse<CustomerBasketOperationResolutionDto>>(JsonOptions);
        result.Should().NotBeNull();
        result!.Success.Should().BeTrue();
        result.Data.Should().NotBeNull();
        return result.Data!;
    }
}
