using System.Net;
using System.Net.Http.Headers;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using RestaurantSystem.Api.Features.Basket.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.IntegrationTests.Common;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Basket;

[Collection("Database Lane 2")]
public sealed class CustomerBasketCancellationAuthorizationTests(DatabaseFixture fixture) : CustomerBasketCancellationFixture(fixture)
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

    [Theory]
    [InlineData(UserRole.Admin)]
    [InlineData(UserRole.Server)]
    [InlineData(UserRole.Cashier)]
    [InlineData(UserRole.KitchenStaff)]
    public async Task Staff_cannot_seal_guest_adds_or_edits(UserRole role)
    {
        var basket = await AddAsync();
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token(role));
        (await CancelAddAsync(AddRequest())).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await CancelMutationAsync(basket, Update(basket))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ReceiptsAsync()).Should().BeEmpty();
    }

    [Theory]
    [InlineData("Bearer malformed-token")]
    [InlineData("Basic rejected")]
    [InlineData("expired")]
    public async Task Failed_present_authentication_never_downgrades_to_a_guest_cancellation(string header)
    {
        var basket = await AddAsync();
        if (header == "expired") header = $"Bearer {Token(UserRole.Customer, expired: true)}";
        Client.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", header).Should().BeTrue();
        (await CancelAddAsync(AddRequest())).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await CancelMutationAsync(basket, Update(basket))).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await ReceiptsAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task A_guest_and_another_customer_cannot_resolve_the_original_customers_receipt()
    {
        var token = Token(UserRole.Customer);
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var request = AddRequest();
        var basket = await BasketAsync(await SendAsync("/api/customer-mobile/basket/items", request));
        var edit = Update(basket);
        await BasketAsync(await MutateAsync(basket, edit));
        foreach (var authorization in new string?[] { null, Token(UserRole.Customer, userId: TestAuthHandler.AdminUserId) })
        {
            Client.DefaultRequestHeaders.Authorization = authorization is null ? null : new AuthenticationHeaderValue("Bearer", authorization);
            (await CancelAddAsync(request)).StatusCode.Should().Be(HttpStatusCode.NotFound);
            (await CancelMutationAsync(basket, edit)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        }
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        (await CancelAddAsync(request, Guid.NewGuid().ToString("D"))).StatusCode.Should().Be(HttpStatusCode.NotFound);
        var applied = await ResolutionAsync(await CancelMutationAsync(basket, edit));
        applied.Outcome.Should().Be(CustomerBasketOperationOutcome.Applied);
        applied.Basket!.Items.Single().Quantity.Should().Be(3);
        (await ReceiptsAsync()).Should().HaveCount(2).And.OnlyContain(receipt => !receipt.IsCancelled);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-session")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    [InlineData("AAAAAAAA-AAAA-4AAA-8AAA-AAAAAAAAAAAA")]
    public async Task Cancellation_requires_the_canonical_nonempty_session(string session)
    {
        (await CancelAddAsync(AddRequest(), session)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReceiptsAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task Missing_and_empty_operation_ids_cannot_create_a_seal()
    {
        foreach (var id in new Guid?[] { null, Guid.Empty })
            (await CancelAddAsync(AddRequest() with { OperationId = id })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReceiptsAsync()).Should().BeEmpty();
    }
    private string Token(UserRole role, bool expired = false, string? userId = null) =>
        CustomerBasketTokens.Create(Factory.Services, role, expired, userId);
}
