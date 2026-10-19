using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Basket.Dtos;
using RestaurantSystem.Api.Settings;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Common;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Basket;

[Collection("Database Lane 2")]
public sealed class CustomerBasketReadTests(DatabaseFixture fixture) : IntegrationTestBase(fixture)
{
    private readonly string _session = Guid.NewGuid().ToString("D");

    protected override void ConfigureTestServices(IServiceCollection services)
    {
        services.PostConfigure<AuthenticationOptions>(options =>
        {
            options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
            options.DefaultForbidScheme = JwtBearerDefaults.AuthenticationScheme;
        });
        services.PostConfigure<CustomerMobileSettings>(settings =>
        {
            settings.Enabled = true;
            settings.TenantId = Guid.NewGuid();
            settings.Lifecycle = "active";
            settings.EnabledCapabilities = ["ordering"];
        });
    }

    [Fact]
    public async Task Expired_signed_customer_token_cannot_turn_an_existing_basket_into_empty_guest_success()
    {
        var id = await SeedBasketAsync(Guid.Parse(TestAuthHandler.UserId), Guid.NewGuid().ToString("D"));
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token(UserRole.Customer));
        (await ReadBasketAsync(await GetAsync())).Id.Should().Be(id);
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token(UserRole.Customer, expired: true));
        var rejected = await GetAsync();
        rejected.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        rejected.Headers.GetValues("Token-Expired").Should().Contain("true");
        // This affirmative legacy control demonstrates the ambiguous envelope the native route avoids.
        var legacy = await ReadBasketAsync(await GetAsync(native: false));
        legacy.Id.Should().BeEmpty();
        legacy.UserId.Should().BeNull();
        (await BasketCountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Guests_only_read_their_own_session_and_reads_never_create_baskets()
    {
        var id = await SeedBasketAsync(null, _session);
        (await ReadBasketAsync(await GetAsync())).Id.Should().Be(id);
        var empty = await ReadBasketAsync(await GetAsync(Guid.NewGuid().ToString("D")));
        empty.Id.Should().BeEmpty();
        empty.Items.Should().BeEmpty();
        empty.Total.Should().Be(0);
        (await BasketCountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task A_known_session_cannot_reveal_another_customer_basket_to_a_guest_or_customer()
    {
        await SeedBasketAsync(Guid.Parse(TestAuthHandler.AdminUserId), _session);
        (await ReadBasketAsync(await GetAsync())).Id.Should().BeEmpty();
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token(UserRole.Customer));
        (await ReadBasketAsync(await GetAsync())).Id.Should().BeEmpty();
    }

    [Theory]
    [InlineData("Bearer malformed-token")]
    [InlineData("Basic rejected-credentials")]
    public async Task Present_rejected_credentials_cannot_downgrade_to_a_guest(string authorization)
    {
        Client.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", authorization).Should().BeTrue();
        (await GetAsync()).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await BasketCountAsync()).Should().Be(0);
    }

    [Theory]
    [InlineData(UserRole.Admin)]
    [InlineData(UserRole.Cashier)]
    [InlineData(UserRole.Server)]
    [InlineData(UserRole.KitchenStaff)]
    public async Task Staff_do_not_use_customer_basket_reads(UserRole role)
    {
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token(role));
        (await GetAsync()).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Theory]
    [InlineData("not-a-session")]
    [InlineData("AAAAAAAA-AAAA-4AAA-8AAA-AAAAAAAAAAAA")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public async Task Native_reads_require_canonical_nonempty_sessions(string session) =>
        (await GetAsync(session)).StatusCode.Should().Be(HttpStatusCode.BadRequest);

    [Fact]
    public async Task New_reads_remain_inert_without_native_ordering_rollout()
    {
        Factory.Services.GetRequiredService<IOptions<CustomerMobileSettings>>().Value.Enabled = false;
        (await GetAsync()).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await BasketCountAsync()).Should().Be(0);
    }

    private string Token(UserRole role, bool expired = false) =>
        CustomerBasketTokens.Create(Factory.Services, role, expired);

    private async Task<Guid> SeedBasketAsync(Guid? userId, string session)
    {
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var basket = new Domain.Entities.Basket { UserId = userId, SessionId = session, CreatedBy = nameof(CustomerBasketReadTests) };
        context.Baskets.Add(basket);
        await context.SaveChangesAsync();
        return basket.Id;
    }

    private async Task<int> BasketCountAsync()
    {
        using var scope = Factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Baskets.CountAsync();
    }

    private async Task<HttpResponseMessage> GetAsync(string? session = null, bool native = true)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get,
            new Uri(native ? "/api/customer-mobile/basket" : "/api/Basket", UriKind.Relative));
        request.Headers.Add("X-Session-Id", session ?? _session);
        return await Client.SendAsync(request);
    }

    private static async Task<BasketDto> ReadBasketAsync(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var envelope = await response.Content.ReadFromJsonAsync<ApiResponse<BasketDto>>(JsonOptions);
        envelope.Should().NotBeNull();
        envelope!.Success.Should().BeTrue();
        return envelope.Data!;
    }
}
