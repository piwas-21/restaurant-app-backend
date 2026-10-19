using System.Net;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RestaurantSystem.Api.Features.Basket.Dtos;
using RestaurantSystem.Api.Features.Basket.Dtos.Requests;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Basket;

[Collection("Database Lane 2")]
public sealed class CustomerBasketChannelTests(DatabaseFixture fixture) : CustomerBasketChannelFixture(fixture)
{
    [Fact]
    public async Task Preview_does_not_create_a_basket_or_receipt_and_empty_switch_is_recoverable()
    {
        var preview = await PreviewAsync();
        preview.BasketId.Should().BeEmpty();
        preview.PurchaseFingerprint.Should().BeEmpty();
        preview.Conflicts.Should().BeEmpty();
        using (var scope = Factory.Services.CreateScope())
            (await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Baskets.CountAsync()).Should().Be(0);
        (await ReceiptsAsync()).Should().BeEmpty();
        var request = Channel();
        var first = await BasketAsync(await SwitchAsync(request));
        first.OrderType.Should().Be(OrderType.Takeaway);
        first.Items.Should().BeEmpty();
        first.Id.Should().NotBeEmpty();
        (await BasketAsync(await SwitchAsync(request))).Id.Should().Be(first.Id);
        (await ReceiptsAsync()).Should().ContainSingle();
    }

    [Fact]
    public async Task Switching_requires_exact_reviewed_conflicts_and_removes_only_confirmed_roots()
    {
        var basket = await AddAsync();
        await MaskAsync((int)OrderChannels.DineIn);
        var preview = await PreviewAsync();
        preview.BasketId.Should().Be(basket.Id);
        preview.PurchaseFingerprint.Should().Be(basket.PurchaseFingerprint);
        preview.Conflicts.Should().ContainSingle().Which.BasketItemId.Should().Be(basket.Items[0].Id!.Value);
        preview.Conflicts[0].AllowedOrderTypes.Should().Equal(OrderType.DineIn);
        var request = Channel(basket);
        (await SwitchAsync(request)).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ReadAsync()).Items.Should().ContainSingle();
        (await ReceiptsAsync()).Should().BeEmpty();
        var result = await BasketAsync(await SwitchAsync(request with { ConfirmedConflictItemIds = [basket.Items[0].Id!.Value] }));
        result.OrderType.Should().Be(OrderType.Takeaway);
        result.Items.Should().BeEmpty();
        result.Total.Should().Be(0);
        (await ReceiptsAsync()).Should().ContainSingle();
    }

    [Fact]
    public async Task Changed_conflicts_are_refused_even_when_the_purchase_digest_is_unchanged()
    {
        var basket = await AddAsync();
        var request = Channel(basket, removals: [basket.Items[0].Id!.Value]);
        await MaskAsync((int)OrderChannels.DineIn);
        (await PreviewAsync()).Conflicts.Should().ContainSingle();
        await MaskAsync(null);
        (await ReadAsync()).PurchaseFingerprint.Should().Be(request.ExpectedPurchaseFingerprint);
        (await SwitchAsync(request)).StatusCode.Should().Be(HttpStatusCode.Conflict);
        var current = await ReadAsync();
        current.Items.Should().ContainSingle();
        current.OrderType.Should().BeNull();
        (await ReceiptsAsync()).Should().BeEmpty();
        (await BasketAsync(await SwitchAsync(request with { ConfirmedConflictItemIds = [] }))).Items.Should().ContainSingle();
    }

    [Fact]
    public async Task Stale_review_and_unknown_root_removals_cannot_change_the_basket_or_create_receipts()
    {
        var basket = await AddAsync();
        var request = Channel(basket);
        await BasketAsync(await MutateAsync(basket, Update(basket)));
        (await SwitchAsync(request)).StatusCode.Should().Be(HttpStatusCode.Conflict);
        var current = await ReadAsync();
        (await SwitchAsync(Channel(current, removals: [Guid.NewGuid()]))).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ReadAsync()).OrderType.Should().BeNull();
        (await ReceiptsAsync()).Should().ContainSingle();
    }

    [Fact]
    public async Task Concurrent_identical_switches_apply_once_and_changed_payloads_cannot_reuse_the_identity()
    {
        var basket = await AddAsync();
        var request = Channel(basket);
        foreach (var response in await Task.WhenAll(SwitchAsync(request), SwitchAsync(request)))
            (await BasketAsync(response)).OrderType.Should().Be(OrderType.Takeaway);
        (await SwitchAsync(request with { OrderType = OrderType.Delivery })).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ReceiptsAsync()).Should().ContainSingle();
        (await CancelChannelAsync(request with { OrderType = OrderType.Delivery })).StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Cancellation_seals_an_unseen_switch_and_does_not_undo_an_accepted_one()
    {
        var unseen = Channel();
        (await ResolutionAsync(await CancelChannelAsync(unseen))).Outcome.Should().Be(CustomerBasketOperationOutcome.Cancelled);
        (await SwitchAsync(unseen)).StatusCode.Should().Be(HttpStatusCode.Conflict);
        var accepted = Channel();
        var basket = await BasketAsync(await SwitchAsync(accepted));
        var resolution = await ResolutionAsync(await CancelChannelAsync(accepted));
        resolution.Outcome.Should().Be(CustomerBasketOperationOutcome.Applied);
        resolution.Basket!.Id.Should().Be(basket.Id);
        resolution.Basket.OrderType.Should().Be(OrderType.Takeaway);
        (await ReceiptsAsync()).Should().HaveCount(2);
    }

    [Fact]
    public async Task Disabled_channels_refuse_new_effects_but_do_not_block_replay_or_sealing()
    {
        var accepted = Channel();
        var basket = await BasketAsync(await SwitchAsync(accepted));
        await EnableAsync(OrderType.Takeaway, false);
        (await SwitchAsync(Channel(basket))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await BasketAsync(await SwitchAsync(accepted))).OrderType.Should().Be(OrderType.Takeaway);
        (await ResolutionAsync(await CancelChannelAsync(Channel(basket)))).Outcome.Should().Be(CustomerBasketOperationOutcome.Cancelled);
        (await ReceiptsAsync()).Should().HaveCount(2);
    }

    [Fact]
    public async Task Clearing_the_original_basket_never_allows_replay_to_recreate_it()
    {
        var request = Channel();
        var basket = await BasketAsync(await SwitchAsync(request));
        using (var scope = Factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            context.Baskets.Remove(await context.Baskets.SingleAsync(item => item.Id == basket.Id));
            await context.SaveChangesAsync();
        }
        (await SwitchAsync(request)).StatusCode.Should().Be(HttpStatusCode.Conflict);
        var proof = await ResolutionAsync(await CancelChannelAsync(request));
        proof.Outcome.Should().Be(CustomerBasketOperationOutcome.Applied);
        proof.Basket.Should().BeNull();
        (await ReadAsync()).Id.Should().BeEmpty();
        (await ReceiptsAsync()).Should().ContainSingle();
    }

    [Fact]
    public async Task Cancellation_racing_execution_has_one_durable_decision()
    {
        var request = Channel();
        var execution = SwitchAsync(request);
        var cancellation = CancelChannelAsync(request);
        var result = await execution;
        var proof = await ResolutionAsync(await cancellation);
        if (proof.Outcome == CustomerBasketOperationOutcome.Cancelled)
        {
            result.StatusCode.Should().Be(HttpStatusCode.Conflict);
            (await ReadAsync()).Id.Should().BeEmpty();
        }
        else
        {
            (await BasketAsync(result)).OrderType.Should().Be(OrderType.Takeaway);
            proof.Basket!.OrderType.Should().Be(OrderType.Takeaway);
        }
        (await ReceiptsAsync()).Should().ContainSingle();
    }
}
