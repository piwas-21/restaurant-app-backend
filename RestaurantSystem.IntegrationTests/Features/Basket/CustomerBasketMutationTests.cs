using System.Net;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Features.Basket.Dtos.Requests;
using RestaurantSystem.Api.Settings;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Basket;

[Collection("Database Lane 2")]
public sealed class CustomerBasketMutationTests(DatabaseFixture fixture) : CustomerBasketMutationFixture(fixture)
{
    [Fact]
    public async Task Identical_concurrent_absolute_edits_commit_once_and_replay_the_current_owned_basket()
    {
        var original = await AddAsync();
        var request = Update(original);
        var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => MutateAsync(original, request)));
        foreach (var response in results)
        {
            var basket = await BasketAsync(response);
            basket.Items.Single().Quantity.Should().Be(3);
            basket.Items.Single().SpecialInstructions.Should().Be("No salt");
            basket.SubTotal.Should().Be(21m);
        }
        var accepted = await BasketAsync(await MutateAsync(original, request));
        await BasketAsync(await MutateAsync(accepted, Update(accepted) with { Quantity = 4 }));
        (await BasketAsync(await MutateAsync(original, request))).Items.Single().Quantity.Should().Be(4);
        (await ReceiptsAsync()).Should().HaveCount(2);
    }

    [Fact]
    public async Task Competing_operations_on_one_reviewed_revision_allow_one_effect_only()
    {
        var basket = await AddAsync();
        var first = Update(basket);
        var results = await Task.WhenAll(MutateAsync(basket, first),
            MutateAsync(basket, first with { OperationId = Guid.NewGuid(), Quantity = 4 }));
        results.Select(result => result.StatusCode).Should().BeEquivalentTo([HttpStatusCode.OK, HttpStatusCode.Conflict]);
        var accepted = await BasketAsync(results.Single(result => result.StatusCode == HttpStatusCode.OK));
        accepted.SubTotal.Should().BeOneOf(21m, 28m);
        (await ReceiptsAsync()).Should().ContainSingle();
    }

    [Fact]
    public async Task Concurrent_removal_of_the_last_root_replays_without_resurrection_or_an_extra_effect()
    {
        var basket = await AddAsync();
        var request = Update(basket) with { Action = CustomerBasketItemAction.Remove, Quantity = null, SpecialInstructions = null };
        var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => MutateAsync(basket, request)));
        foreach (var response in results)
        {
            var empty = await BasketAsync(response);
            empty.Id.Should().Be(basket.Id);
            empty.Items.Should().BeEmpty();
            empty.SubTotal.Should().Be(0);
            empty.Total.Should().Be(0);
        }
        (await ReceiptsAsync()).Should().ContainSingle();
    }

    [Fact]
    public async Task Stale_review_and_wrong_basket_refuse_changes_and_do_not_consume_the_operation_id()
    {
        var basket = await AddAsync();
        var request = Update(basket);
        var newer = await AddAsync();
        (await MutateAsync(basket, request)).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await MutateAsync(newer, Update(newer) with { ExpectedBasketId = Guid.NewGuid() })).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ReceiptsAsync()).Should().BeEmpty();
        var result = await BasketAsync(await MutateAsync(newer, request with { ExpectedPurchaseFingerprint = newer.PurchaseFingerprint }));
        result.Items.Single().Quantity.Should().Be(3);
        (await ReceiptsAsync()).Should().ContainSingle();
    }

    [Fact]
    public async Task Changed_payload_route_or_kind_cannot_reuse_an_accepted_identity()
    {
        var basket = await AddAsync();
        var request = Update(basket);
        await BasketAsync(await MutateAsync(basket, request));
        foreach (var changed in new[] {
            request with { Quantity = 4 }, request with { SpecialInstructions = "Different" },
            request with { Action = CustomerBasketItemAction.Remove, Quantity = null, SpecialInstructions = null },
            request with { ExpectedBasketId = Guid.NewGuid() }
        }) (await MutateAsync(basket, changed)).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await MutateAsync(basket, request, itemId: Guid.NewGuid())).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await SendAsync("/api/customer-mobile/basket/items", new AddToBasketDto
        {
            OperationId = request.OperationId,
            ProductId = ProductId,
            Quantity = 1
        })).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ReceiptsAsync()).Should().ContainSingle();
    }

    [Fact]
    public async Task Recovery_survives_rollout_withdrawal_but_new_edits_are_refused()
    {
        var basket = await AddAsync();
        var request = Update(basket);
        var accepted = await BasketAsync(await MutateAsync(basket, request));
        Factory.Services.GetRequiredService<IOptions<CustomerMobileSettings>>().Value.Enabled = false;
        (await BasketAsync(await MutateAsync(basket, request))).Items.Single().Quantity.Should().Be(3);
        (await MutateAsync(accepted, Update(accepted))).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await ReceiptsAsync()).Should().ContainSingle();
    }

    [Fact]
    public async Task Cleanup_keeps_the_receipt_and_cannot_recreate_an_erased_basket()
    {
        var basket = await AddAsync();
        var request = Update(basket);
        await BasketAsync(await MutateAsync(basket, request));
        using (var scope = Factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await context.BasketItems.Where(item => item.BasketId == basket.Id).ExecuteDeleteAsync();
            await context.Baskets.Where(row => row.Id == basket.Id).ExecuteDeleteAsync();
        }
        (await MutateAsync(basket, request)).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ReceiptsAsync()).Should().ContainSingle();
    }

    [Fact]
    public async Task Nested_children_cannot_be_edited_or_removed_independently()
    {
        var basket = await AddAsync();
        Guid childId;
        using (var scope = Factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var child = new BasketItem
            {
                BasketId = basket.Id,
                ParentBasketItemId = basket.Items[0].Id,
                ProductId = ProductId,
                Quantity = 2,
                UnitPrice = 0,
                ItemTotal = 0,
                CreatedBy = nameof(CustomerBasketMutationTests)
            };
            context.BasketItems.Add(child);
            await context.SaveChangesAsync();
            childId = child.Id;
        }
        // Add's readback supplies the real persisted nested fingerprint, rather than a computed test digest.
        basket = await AddAsync();
        var update = Update(basket);
        (await MutateAsync(basket, update, childId)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await MutateAsync(basket, update with { Action = CustomerBasketItemAction.Remove, Quantity = null, SpecialInstructions = null }, childId))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await ReceiptsAsync()).Should().BeEmpty();
    }
}
