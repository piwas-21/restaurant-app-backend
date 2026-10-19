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
public sealed class CustomerBasketChannelAuthorizationTests(DatabaseFixture fixture) : CustomerBasketChannelFixture(fixture)
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
    [InlineData(UserRole.Cashier)]
    [InlineData(UserRole.Server)]
    [InlineData(UserRole.KitchenStaff)]
    public async Task Staff_cannot_preview_switch_or_seal_a_guest_channel(UserRole role)
    {
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token(role));
        await RefusedAsync(HttpStatusCode.Forbidden);
    }

    [Theory]
    [InlineData("Bearer malformed-token")]
    [InlineData("Basic rejected")]
    [InlineData("expired")]
    public async Task Rejected_present_authentication_never_downgrades_to_guest_access(string header)
    {
        if (header == "expired") header = $"Bearer {Token(UserRole.Customer, expired: true)}";
        Client.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", header).Should().BeTrue();
        await RefusedAsync(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-session")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    [InlineData("AAAAAAAA-AAAA-4AAA-8AAA-AAAAAAAAAAAA")]
    public async Task All_native_channel_routes_require_the_canonical_session(string session)
    {
        await RefusedAsync(HttpStatusCode.BadRequest, session);
    }

    [Fact]
    public async Task Guest_and_another_customer_cannot_read_or_replay_the_original_customer_receipt()
    {
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token(UserRole.Customer));
        var basket = await AddAsync();
        var request = Channel(basket);
        await BasketAsync(await SwitchAsync(request));
        foreach (var user in new string?[] { null, TestAuthHandler.AdminUserId })
        {
            Client.DefaultRequestHeaders.Authorization = user is null ? null
                : new AuthenticationHeaderValue("Bearer", Token(UserRole.Customer, userId: user));
            (await PreviewAsync()).BasketId.Should().BeEmpty();
            (await SwitchAsync(request)).StatusCode.Should().Be(HttpStatusCode.NotFound);
            (await CancelChannelAsync(request)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        }
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token(UserRole.Customer));
        (await BasketAsync(await SwitchAsync(request))).Id.Should().Be(basket.Id);
        (await ReceiptsAsync()).Should().ContainSingle();
    }

    [Fact]
    public async Task Other_guest_sessions_cannot_resolve_the_original_operation()
    {
        var request = Channel();
        await BasketAsync(await SwitchAsync(request));
        var other = Guid.NewGuid().ToString("D");
        (await SwitchAsync(request, other)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await CancelChannelAsync(request, other)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await ReceiptsAsync()).Should().ContainSingle();
    }

    private async Task RefusedAsync(HttpStatusCode status, string? session = null)
    {
        (await SwitchAsync(Channel(), session)).StatusCode.Should().Be(status);
        (await CancelChannelAsync(Channel(), session)).StatusCode.Should().Be(status);
        using var request = new HttpRequestMessage(HttpMethod.Get,
            new Uri("/api/customer-mobile/basket/channel-preview?orderType=Takeaway", UriKind.Relative));
        request.Headers.Add("X-Session-Id", session ?? Session);
        (await Client.SendAsync(request)).StatusCode.Should().Be(status);
        (await ReceiptsAsync()).Should().BeEmpty();
    }
    private string Token(UserRole role, bool expired = false, string? userId = null) =>
        CustomerBasketTokens.Create(Factory.Services, role, expired, userId);
}
