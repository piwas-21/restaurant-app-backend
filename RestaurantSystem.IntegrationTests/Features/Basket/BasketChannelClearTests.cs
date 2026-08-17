using System.Net;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Basket.Dtos;
using RestaurantSystem.Api.Features.Basket.Dtos.Requests;
using RestaurantSystem.Api.Features.Basket.Interfaces;
using RestaurantSystem.Domain.Common;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Basket;

/// <summary>
/// DELETE /api/Basket/order-type — plan §9.17.
/// </summary>
/// <remarks>
/// The gap: the client clears its own channel on two paths (the 24h TTL, and
/// <c>useOrderTypeEnabledGuard</c> finding the chosen channel disabled) but had no way to tell the
/// SERVER, because the PUT takes a non-nullable <c>[JsonRequired]</c> order type. The basket stayed
/// armed on the abandoned channel and every later add was judged against it — so a guest holding no
/// channel could still be refused for one, with no way to fix it.
/// <para>
/// <see cref="Clearing_lets_a_previously_blocked_item_be_added"/> is the load-bearing test: it
/// asserts the JOURNEY rather than the column, so a clear that writes null without actually
/// disarming the guard would still fail it.
/// </para>
/// </remarks>
[Collection("Database Lane 4")]
public class BasketChannelClearTests : IntegrationTestBase
{
    private const int TakeawayAndDelivery = (int)(OrderChannels.Takeaway | OrderChannels.Delivery);

    private readonly string _sessionId = Guid.NewGuid().ToString();
    private Guid _pizzaId;

    public BasketChannelClearTests(DatabaseFixture databaseFixture)
        : base(databaseFixture)
    {
    }

    protected override async Task SeedTestData()
    {
        await base.SeedTestData();

        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        _pizzaId = (await context.Products.FirstAsync(p => p.Name == "Test Pizza")).Id;

        // The pizza inherits "takeaway & delivery only" from its primary category, so it is BLOCKED
        // on dine-in. That is what makes the journey test discriminating.
        var mains = await context.Categories.FirstAsync(c => c.Name == "Main Course");
        mains.AvailableOrderTypes = TakeawayAndDelivery;

        context.ProductCategories.Add(new ProductCategory
        {
            ProductId = _pizzaId,
            CategoryId = mains.Id,
            IsPrimary = true,
            CreatedBy = "seed"
        });

        await context.SaveChangesAsync();
    }

    [Fact]
    public async Task Clearing_lets_a_previously_blocked_item_be_added()
    {
        // The §9.17 journey end to end. Arm the basket on dine-in, confirm the guard genuinely
        // refuses the dine-in-blocked pizza, clear the channel, then add the SAME product again.
        await SetChannelAsync(OrderType.DineIn);

        var blocked = await Record.ExceptionAsync(() => AddPizzaAsync());
        blocked.Should().BeOfType<BadRequestException>(
            "the guard must actually be armed, or the retry below proves nothing");

        await ClearChannelAsync();

        // The real assertion: not "the column is null" but "the thing the guest could not do, they
        // can now do". A clear that nulled the column without disarming the guard fails here.
        var afterClear = await Record.ExceptionAsync(() => AddPizzaAsync());
        afterClear.Should().BeNull("no channel is UNRESTRICTED, so nothing is blocked");
    }

    [Fact]
    public async Task Clearing_nulls_the_persisted_channel()
    {
        await SetChannelAsync(OrderType.Takeaway);
        (await PersistedOrderTypeAsync()).Should().Be(OrderType.Takeaway);

        var result = await ClearChannelAsync();

        result.Should().NotBeNull();
        result!.OrderType.Should().BeNull("the returned basket is what the client reconciles against");
        (await PersistedOrderTypeAsync()).Should().BeNull("and the row itself must be disarmed");
    }

    [Fact]
    public async Task Clearing_keeps_every_line()
    {
        // Non-destructive is the whole difference from the SET path, which may delete conflicting
        // lines. Clearing cannot conflict — null is permissive — so it must never remove anything.
        await AddPizzaAsync();
        await SetChannelAsync(OrderType.Takeaway);

        var result = await ClearChannelAsync();

        result!.Items.Should().ContainSingle("clearing the channel is not a basket edit");
        (await PersistedLineCountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Clearing_is_idempotent_and_the_second_call_writes_NOTHING()
    {
        await SetChannelAsync(OrderType.Delivery);

        await ClearChannelAsync();
        var afterFirst = await PersistedUpdatedAtAsync();

        var second = await ClearChannelAsync();

        second.Should().NotBeNull("a basket with no channel is still a basket");
        second!.OrderType.Should().BeNull();
        // The write-skip is the POINT of the `OrderType is null` early return, and asserting only
        // the two lines above cannot see it — a branch that re-writes the row satisfies them both.
        // Verified by deleting that branch: without this assertion the test still passed.
        (await PersistedUpdatedAtAsync()).Should().Be(afterFirst,
            "a no-op clear must not churn the audit columns and pass as real activity");
    }

    [Fact]
    public async Task Clearing_without_a_basket_succeeds_and_does_NOT_create_one()
    {
        // "Nothing to clear" is the outcome the caller asked for, so it is a success, not a 404.
        // The second half is the one worth pinning: the SET path upserts a basket by design (§9.13),
        // and reusing that shape here would strew orphan rows under session ids no guest returns to.
        var result = await ClearChannelAsync();

        result.Should().BeNull();
        (await BasketExistsAsync()).Should().BeFalse("clearing must not bring a basket into existence");
    }

    [Fact]
    public async Task The_clear_route_resolves_and_clears_THE_SESSIONS_OWN_basket()
    {
        // §9.7's lesson: the URL is the contract, and nothing else in the suite drives this verb.
        // A route that 404s looks exactly like a client bug while every handler test still passes.
        //
        // It must clear a REAL basket, not an absent one. Driving this against a fresh session
        // exercises only the "no basket" branch, where the answer is null whichever basket the
        // header resolved to — so a controller that read X-Session-Id and then discarded it still
        // passed. Verified: replacing `command.SessionId = sessionId` with a random GUID left all
        // seven green. Seeding a channel on THIS session makes the wiring load-bearing.
        //
        // Asserting the payload rather than `Success` for the same reason BasketRoutingContractTests
        // calls out: the handler answers SuccessWithData on every branch, so `Success` cannot fail.
        // ANONYMOUS on purpose, and it is load-bearing rather than incidental. TestAuthHandler
        // authenticates by default, and BasketRepository.ApplyOwnerFilter PREFERS UserId and
        // ignores the session header entirely for an authenticated caller — so under the default
        // identity the header is not what resolves the basket, and the discard mutant survives.
        // Anonymous is also the real shape of this journey: the guest whose channel got cleared by
        // the TTL or the enabled-list guard is a session, not an account.
        AuthenticateAsAnonymous();
        await SetChannelAsync(OrderType.Takeaway);
        Client.DefaultRequestHeaders.Add("X-Session-Id", _sessionId);

        var response = await Client.DeleteAsync("/api/Basket/order-type");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await ReadResponseAsync<ApiResponse<BasketDto?>>(response);
        body!.Data.Should().NotBeNull("the header named a basket that exists");
        body.Data!.OrderType.Should().BeNull();
        (await PersistedOrderTypeAsync()).Should().BeNull("and the right row was the one disarmed");
    }

    [Fact]
    public async Task The_clear_route_requires_a_session_header()
    {
        var response = await Client.DeleteAsync("/api/Basket/order-type");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "the shared MissingSession guard must front this action like the other six");
    }

    // --- helpers ---

    private async Task AddPizzaAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var basketService = scope.ServiceProvider.GetRequiredService<IBasketService>();
        await basketService.AddItemToBasketAsync(_sessionId, null,
            new AddToBasketDto { ProductId = _pizzaId, Quantity = 1 });
    }

    private async Task SetChannelAsync(OrderType orderType)
    {
        using var scope = Factory.Services.CreateScope();
        var channelService = scope.ServiceProvider.GetRequiredService<IBasketChannelService>();
        await channelService.SetOrderTypeAsync(_sessionId, null, orderType, removeConflicts: true);
    }

    private async Task<BasketDto?> ClearChannelAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var channelService = scope.ServiceProvider.GetRequiredService<IBasketChannelService>();
        return await channelService.ClearOrderTypeAsync(_sessionId, null);
    }

    private async Task<OrderType?> PersistedOrderTypeAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return (await context.Baskets.AsNoTracking()
            .FirstAsync(b => b.SessionId == _sessionId)).OrderType;
    }

    private async Task<DateTime?> PersistedUpdatedAtAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return (await context.Baskets.AsNoTracking()
            .FirstAsync(b => b.SessionId == _sessionId)).UpdatedAt;
    }

    private async Task<int> PersistedLineCountAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await context.BasketItems.AsNoTracking()
            .CountAsync(i => i.Basket!.SessionId == _sessionId);
    }

    private async Task<bool> BasketExistsAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await context.Baskets.AsNoTracking().AnyAsync(b => b.SessionId == _sessionId);
    }
}
