using System.Net;
using System.Net.Http.Headers;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.IntegrationTests.Common;
using RestaurantSystem.IntegrationTests.Features.Basket;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

[Collection("Database Lane 4")]
public sealed class CustomerCheckoutAuthorizationTests(DatabaseFixture fixture) : CustomerCheckoutFixture(fixture)
{
    protected override void ConfigureTestServices(IServiceCollection services)
    {
        base.ConfigureTestServices(services);
        services.PostConfigure<AuthenticationOptions>(options =>
        {
            options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
            options.DefaultForbidScheme = JwtBearerDefaults.AuthenticationScheme;
        });
    }

    [Fact]
    public async Task Staff_cannot_use_the_customer_quote_contract()
    {
        var basket = await CheckoutBasketAsync();
        foreach (var role in new[] { UserRole.Admin, UserRole.Server, UserRole.Cashier, UserRole.KitchenStaff })
        {
            Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token(role));
            (await QuoteResponseAsync(Review(basket))).StatusCode.Should().Be(HttpStatusCode.Forbidden, role.ToString());
        }
        Client.DefaultRequestHeaders.Authorization = null;
        (await QuoteAsync(Review(basket))).Money.Total.Should().Be(17m);
        await AssertNoFinancialEffectsAsync();
    }

    [Fact]
    public async Task Rejected_authentication_cannot_fall_back_to_a_known_guest_session()
    {
        var basket = await CheckoutBasketAsync();
        foreach (var header in new[] { "Bearer malformed-token", "Basic rejected", $"Bearer {Token(UserRole.Customer, expired: true)}" })
        {
            Client.DefaultRequestHeaders.Remove("Authorization");
            Client.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", header).Should().BeTrue();
            (await QuoteResponseAsync(Review(basket))).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }
        Client.DefaultRequestHeaders.Remove("Authorization");
        (await QuoteAsync(Review(basket))).BasketId.Should().Be(basket.Id);
        await AssertNoFinancialEffectsAsync();
    }

    [Fact]
    public async Task A_known_session_cannot_reveal_an_authenticated_customer_basket_to_another_owner()
    {
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token(UserRole.Customer));
        var basket = await CheckoutBasketAsync();
        var quote = await QuoteAsync(Review(basket));
        quote.BasketId.Should().Be(basket.Id);
        foreach (var user in new string?[] { null, TestAuthHandler.AdminUserId })
        {
            Client.DefaultRequestHeaders.Authorization = user is null ? null
                : new AuthenticationHeaderValue("Bearer", Token(UserRole.Customer, userId: user));
            (await QuoteResponseAsync(Review(basket))).StatusCode.Should().Be(HttpStatusCode.Conflict);
        }
        await AssertNoFinancialEffectsAsync();
    }

    [Fact]
    public async Task Canonical_nonempty_sessions_are_required_even_for_authenticated_customers()
    {
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token(UserRole.Customer));
        var basket = await CheckoutBasketAsync();
        foreach (var session in new[] { "", "not-a-session", "00000000-0000-0000-0000-000000000000", "AAAAAAAA-AAAA-4AAA-8AAA-AAAAAAAAAAAA" })
            (await QuoteResponseAsync(Review(basket), session)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await QuoteAsync(Review(basket))).BasketId.Should().Be(basket.Id);
        await AssertNoFinancialEffectsAsync();
    }

    private string Token(UserRole role, bool expired = false, string? userId = null) =>
        CustomerBasketTokens.Create(Factory.Services, role, expired, userId);
}
