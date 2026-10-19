using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RestaurantSystem.Api.Features.Basket.Dtos.Requests;
using RestaurantSystem.Api.Settings;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Basket;

[Collection("Database Lane 2")]
public sealed class CustomerBasketAddRateLimitTests(DatabaseFixture fixture) : IntegrationTestBase(fixture)
{
    protected override void ConfigureTestServices(IServiceCollection services) =>
        services.PostConfigure<BasketAddRateLimitSettings>(settings => settings.AttemptsPerIpPerMinute = 1);

    [Fact]
    public async Task A_distinct_ip_keeps_its_own_budget_while_the_original_ip_is_limited()
    {
        var firstIp = IPAddress.Parse("192.0.2.1");
        var secondIp = IPAddress.Parse("192.0.2.2");
        (await FromIpAsync(firstIp)).Should().Be((int)HttpStatusCode.BadRequest);
        (await FromIpAsync(secondIp)).Should().Be((int)HttpStatusCode.BadRequest);
        (await FromIpAsync(firstIp)).Should().Be((int)HttpStatusCode.TooManyRequests);
    }

    private async Task<int> FromIpAsync(IPAddress address)
    {
        using var body = new MemoryStream(JsonSerializer.SerializeToUtf8Bytes(new AddToBasketDto { OperationId = Guid.Empty }));
        var context = await Factory.Server.SendAsync(http =>
        {
            http.Connection.RemoteIpAddress = address;
            http.Request.Scheme = "https";
            http.Request.Method = "POST";
            http.Request.Path = "/api/customer-mobile/basket/items";
            http.Request.Headers["X-Session-Id"] = Guid.NewGuid().ToString("D");
            http.Request.ContentType = "application/json";
            http.Request.Body = body;
        });
        return context.Response.StatusCode;
    }

    [Theory]
    [InlineData("/api/customer-mobile/basket/items", "/api/basket/items")]
    [InlineData("/api/basket/items", "/api/customer-mobile/basket/items")]
    [InlineData("/api/customer-mobile/basket/items", "/api/customer-mobile/basket/items/11111111-1111-4111-8111-111111111111/operations")]
    [InlineData("/api/customer-mobile/basket/items/11111111-1111-4111-8111-111111111111/operations", "/api/basket/items")]
    public async Task Native_mutations_and_both_add_routes_share_an_ip_budget_that_rotating_sessions_cannot_escape(string first, string second)
    {
        AuthenticateAsAnonymous();
        Client.DefaultRequestHeaders.Add("X-Session-Id", Guid.NewGuid().ToString("D"));
        (await PostAsJsonAsync(first, new AddToBasketDto { OperationId = Guid.Empty })).StatusCode
            .Should().Be(HttpStatusCode.BadRequest);
        Client.DefaultRequestHeaders.Remove("X-Session-Id");
        Client.DefaultRequestHeaders.Add("X-Session-Id", Guid.NewGuid().ToString("D"));
        var limited = await PostAsJsonAsync(second, new AddToBasketDto
        {
            OperationId = Guid.NewGuid(),
            ProductId = Guid.NewGuid(),
            Quantity = 1
        });
        limited.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        limited.Headers.RetryAfter.Should().NotBeNull();
        limited.Headers.RetryAfter!.Delta.Should().BeGreaterThan(TimeSpan.Zero);
        using var verify = Factory.Services.CreateScope();
        var context = verify.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        (await context.Baskets.CountAsync()).Should().Be(0);
        (await context.Set<CustomerBasketOperation>().CountAsync()).Should().Be(0);
    }
}
