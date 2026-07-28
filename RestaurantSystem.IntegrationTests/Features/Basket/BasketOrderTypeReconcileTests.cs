using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.Basket.Dtos.Requests;
using RestaurantSystem.Api.Features.Basket.Interfaces;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Basket;

/// <summary>
/// §9.13 — the client could ASSERT the basket's channel but never RECONCILE it.
/// </summary>
/// <remarks>
/// Two halves, both here. <c>BasketDto</c> carried no <c>orderType</c>, so nothing on the wire let
/// the client ask what the server actually had — a channel changed in another tab, or cleared by the
/// login merge (G11), was invisible. And <c>PUT /api/Basket/order-type</c> 404'd on an empty cart
/// <b>by construction</b>, because only the add path ever created a basket: a guest who picked a
/// channel before adding anything had the choice silently dropped.
/// </remarks>
public class BasketOrderTypeReconcileTests : IntegrationTestBase
{
    private readonly string _sessionId = Guid.NewGuid().ToString();
    private Guid _colaId;

    public BasketOrderTypeReconcileTests(DatabaseFixture databaseFixture)
        : base(databaseFixture)
    {
    }

    protected override async Task SeedTestData()
    {
        await base.SeedTestData();

        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        _colaId = (await context.Products.FirstAsync(p => p.Name == "Test Cola")).Id;
    }

    [Fact]
    public async Task SettingTheChannel_OnAnEmptyCart_CreatesTheBasketInsteadOf404ing()
    {
        var result = await SetOrderTypeAsync(OrderType.DineIn);

        result.Applied.Should().BeTrue("an empty basket has no lines to conflict");

        // Assert through the RESPONSE, not only the database. Without this, making
        // MapFullGraphAsync return null unconditionally survives every test in this file while the
        // endpoint ships `basket: null` on every call — and the echoed basket is the entire point of
        // §9.13, so it is the one field that must not be pinned by the DB probe alone.
        result.Basket.Should().NotBeNull("the echoed basket is what the client reconciles against");
        result.Basket!.OrderType.Should().Be(OrderType.DineIn);

        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var basket = await context.Baskets.FirstOrDefaultAsync(b => b.SessionId == _sessionId && !b.IsDeleted);

        basket.Should().NotBeNull("the endpoint upserts now");
        basket!.OrderType.Should().Be(OrderType.DineIn);
    }

    /// <summary>
    /// The regression guard for the tracking trap. <c>GetOrCreateBasketAsync</c> returns a TRACKED
    /// entity on its create path but an UNTRACKED one on its find path
    /// (<c>FindBasketAsync</c> is <c>AsNoTracking</c>), so simplifying the upsert to call it
    /// unconditionally would silently drop this write on every basket that already exists — the
    /// PR #89 class, which throws nothing and just does not save.
    /// </summary>
    [Fact]
    public async Task SettingTheChannel_OnAnEXISTINGBasket_StillPersists()
    {
        await AddItemAsync();

        await SetOrderTypeAsync(OrderType.Takeaway);

        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var basket = await context.Baskets.AsNoTracking()
            .FirstAsync(b => b.SessionId == _sessionId && !b.IsDeleted);

        basket.OrderType.Should().Be(OrderType.Takeaway,
            "an untracked basket would accept the assignment in memory and save nothing");
    }

    [Fact]
    public async Task TheBasketPayload_CarriesTheChannel_SoTheClientCanReconcileIt()
    {
        await AddItemAsync();
        await SetOrderTypeAsync(OrderType.Delivery);

        using var scope = Factory.Services.CreateScope();
        var basketService = scope.ServiceProvider.GetRequiredService<IBasketService>();
        var basket = await basketService.GetBasketAsync(_sessionId, null);

        basket!.OrderType.Should().Be(OrderType.Delivery,
            "without this the client can only assert a channel, never read one back");
    }

    [Fact]
    public async Task TheBasketPayload_ReportsNull_WhenNoChannelHasBeenChosen()
    {
        // Null is the permissive browse state, and it has to be distinguishable from "not told" —
        // the client uses exactly this to decide whether to re-assert its own stored choice.
        await AddItemAsync();

        using var scope = Factory.Services.CreateScope();
        var basketService = scope.ServiceProvider.GetRequiredService<IBasketService>();
        var basket = await basketService.GetBasketAsync(_sessionId, null);

        basket!.OrderType.Should().BeNull();
    }

    /// <summary>
    /// The upsert removed the <c>NotFoundException</c> that used to reject this input for free.
    /// Without an explicit guard, both identifiers empty CREATES a basket under a random session id
    /// that no later request can name — an orphan row, answered as <c>Applied = true</c> with a null
    /// <c>Basket</c>, i.e. a success response for a write nobody can ever observe.
    /// </summary>
    [Fact]
    public async Task SettingTheChannel_WithNoSessionAndNoUser_IsRejected_NotSilentlyOrphaned()
    {
        using var scope = Factory.Services.CreateScope();
        var channelService = scope.ServiceProvider.GetRequiredService<IBasketChannelService>();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var before = await context.Baskets.CountAsync();

        var act = async () => await channelService.SetOrderTypeAsync(
            null!, null, OrderType.DineIn, removeConflicts: false);

        await act.Should().ThrowAsync<BadRequestException>();
        (await context.Baskets.CountAsync()).Should().Be(before, "no orphan may be written");
    }

    private async Task AddItemAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var basketService = scope.ServiceProvider.GetRequiredService<IBasketService>();
        await basketService.AddItemToBasketAsync(_sessionId, null,
            new AddToBasketDto { ProductId = _colaId, Quantity = 1 });
    }

    private async Task<Api.Features.Basket.Dtos.BasketChannelSwitchDto> SetOrderTypeAsync(OrderType orderType)
    {
        using var scope = Factory.Services.CreateScope();
        var channelService = scope.ServiceProvider.GetRequiredService<IBasketChannelService>();
        return await channelService.SetOrderTypeAsync(_sessionId, null, orderType, removeConflicts: false);
    }
}
