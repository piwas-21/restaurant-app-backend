using System.Net;
using System.Net.Http.Headers;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.IntegrationTests.Common;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Basket;

[Collection("Database Lane 2")]
public sealed class CustomerBasketMutationAuthorizationTests(DatabaseFixture fixture) : CustomerBasketMutationFixture(fixture)
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
    public async Task A_customer_basket_cannot_be_mutated_by_a_guest_with_its_exact_session()
    {
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token(UserRole.Customer));
        var basket = await AddAsync();
        var request = Update(basket);
        Client.DefaultRequestHeaders.Authorization = null;
        (await MutateAsync(basket, request)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await ReceiptsAsync()).Should().BeEmpty();
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token(UserRole.Customer));
        (await BasketAsync(await MutateAsync(basket, request))).Items.Single().Quantity.Should().Be(3);
    }

    [Fact]
    public async Task Another_signed_customer_cannot_apply_or_replay_the_original_customers_operation()
    {
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token(UserRole.Customer));
        var basket = await AddAsync();
        var request = Update(basket);
        var accepted = await BasketAsync(await MutateAsync(basket, request));
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            Token(UserRole.Customer, userId: TestAuthHandler.AdminUserId));
        (await MutateAsync(basket, request)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await MutateAsync(accepted, Update(accepted))).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await ReceiptsAsync()).Should().ContainSingle();
    }

    [Fact]
    public async Task Expired_signed_authentication_refuses_new_effects_and_recovery_before_guest_downgrade()
    {
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token(UserRole.Customer));
        var basket = await AddAsync();
        var request = Update(basket);
        var accepted = await BasketAsync(await MutateAsync(basket, request));
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token(UserRole.Customer, expired: true));
        foreach (var value in new[] { request, Update(accepted) })
        {
            var response = await MutateAsync(basket, value);
            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
            response.Headers.GetValues("Token-Expired").Should().Contain("true");
        }
        (await ReceiptsAsync()).Should().ContainSingle();
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token(UserRole.Customer));
        (await BasketAsync(await MutateAsync(basket, request))).Items.Single().Quantity.Should().Be(3);
    }

    [Theory]
    [InlineData(UserRole.Admin)]
    [InlineData(UserRole.Server)]
    [InlineData(UserRole.Cashier)]
    [InlineData(UserRole.KitchenStaff)]
    public async Task Staff_cannot_mutate_a_guest_basket(UserRole role)
    {
        var basket = await AddAsync();
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token(role));
        (await MutateAsync(basket, Update(basket))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ReceiptsAsync()).Should().BeEmpty();
    }

    [Theory]
    [InlineData("Bearer malformed-token")]
    [InlineData("Basic rejected")]
    public async Task Present_failed_authentication_cannot_borrow_the_guest_capability(string header)
    {
        var basket = await AddAsync();
        Client.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", header).Should().BeTrue();
        (await MutateAsync(basket, Update(basket))).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await ReceiptsAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task Different_guest_sessions_cannot_apply_or_replay_an_operation()
    {
        var basket = await AddAsync();
        var request = Update(basket);
        var other = Guid.NewGuid().ToString("D");
        (await MutateAsync(basket, request, session: other)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        await BasketAsync(await MutateAsync(basket, request));
        (await MutateAsync(basket, request, session: other)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await ReceiptsAsync()).Should().ContainSingle();
    }

    private string Token(UserRole role, bool expired = false, string? userId = null) =>
        CustomerBasketTokens.Create(Factory.Services, role, expired, userId);
}
