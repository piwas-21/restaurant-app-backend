using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Basket.Dtos;
using RestaurantSystem.Api.Features.Basket.Dtos.Requests;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Basket;

public abstract class CustomerBasketChannelFixture(DatabaseFixture fixture) : CustomerBasketCancellationFixture(fixture)
{
    protected static CustomerBasketChannelDto Channel(BasketDto? basket = null, OrderType type = OrderType.Takeaway,
        IReadOnlyList<Guid>? removals = null) => new()
        {
            OperationId = Guid.NewGuid(),
            ExpectedBasketId = basket?.Id ?? Guid.Empty,
            ExpectedPurchaseFingerprint = basket?.PurchaseFingerprint ?? string.Empty,
            OrderType = type,
            ConfirmedConflictItemIds = removals ?? []
        };
    protected Task<HttpResponseMessage> SwitchAsync(CustomerBasketChannelDto request, string? session = null) =>
        SendAsync("/api/customer-mobile/basket/channel", request, session);
    protected Task<HttpResponseMessage> CancelChannelAsync(CustomerBasketChannelDto request, string? session = null) =>
        SendAsync("/api/customer-mobile/basket/channel/cancel", request, session);
    protected async Task<CustomerBasketChannelPreviewDto> PreviewAsync(OrderType type = OrderType.Takeaway)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get,
            new Uri($"/api/customer-mobile/basket/channel-preview?orderType={type}", UriKind.Relative));
        request.Headers.Add("X-Session-Id", Session);
        var response = await Client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<ApiResponse<CustomerBasketChannelPreviewDto>>(JsonOptions))!.Data!;
    }
    protected async Task<BasketDto> ReadAsync()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("/api/customer-mobile/basket", UriKind.Relative));
        request.Headers.Add("X-Session-Id", Session);
        return await BasketAsync(await Client.SendAsync(request));
    }
    protected async Task MaskAsync(int? mask)
    {
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        (await context.Products.SingleAsync(product => product.Id == ProductId)).AvailableOrderTypes = mask;
        await context.SaveChangesAsync();
    }
    protected async Task EnableAsync(OrderType type, bool enabled)
    {
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        (await context.OrderTypeConfigurations.SingleAsync(item => item.OrderType == type)).IsEnabled = enabled;
        await context.SaveChangesAsync();
    }
}
