using System.Net;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RestaurantSystem.Api.Features.Basket.Dtos;
using RestaurantSystem.Api.Features.Basket.Dtos.Requests;
using RestaurantSystem.Api.Settings;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Basket;

[Collection("Database Lane 2")]
public sealed class CustomerBasketCancellationTests(DatabaseFixture fixture) : CustomerBasketCancellationFixture(fixture)
{
    [Fact]
    public async Task Cancelling_an_unseen_add_seals_both_execution_routes_without_creating_a_basket()
    {
        var request = AddRequest();
        foreach (var attempt in Enumerable.Range(0, 2))
        {
            var resolution = await ResolutionAsync(await CancelAddAsync(request));
            resolution.OperationId.Should().Be(request.OperationId!.Value);
            resolution.Outcome.Should().Be(CustomerBasketOperationOutcome.Cancelled);
            resolution.Basket.Should().BeNull();
        }
        foreach (var route in new[] { "/api/customer-mobile/basket/items", "/api/basket/items" })
            (await SendAsync(route, request)).StatusCode.Should().Be(HttpStatusCode.Conflict);
        using var context = DatabaseFixture.CreateContext();
        (await context.Baskets.CountAsync()).Should().Be(0);
        (await context.BasketItems.CountAsync()).Should().Be(0);
        var receipt = (await ReceiptsAsync()).Should().ContainSingle().Subject;
        receipt.IsCancelled.Should().BeTrue();
        receipt.BasketId.Should().BeEmpty();
        var next = await BasketAsync(await SendAsync("/api/customer-mobile/basket/items", AddRequest()));
        next.Items.Single().Quantity.Should().Be(2);
    }

    [Fact]
    public async Task Cancelling_an_applied_add_reports_the_current_original_basket_without_undoing_it()
    {
        var request = AddRequest();
        var basket = await BasketAsync(await SendAsync("/api/customer-mobile/basket/items", request));
        using (var scope = Factory.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<RestaurantSystem.Api.Features.Basket.Interfaces.IBasketService>()
                .UpdateBasketItemAsync(Session, basket.Items[0].Id!.Value, new() { Quantity = 5, SpecialInstructions = "Web edit" });
        var resolution = await ResolutionAsync(await CancelAddAsync(request));
        resolution.OperationId.Should().Be(request.OperationId!.Value);
        resolution.Outcome.Should().Be(CustomerBasketOperationOutcome.Applied);
        resolution.Basket!.Id.Should().Be(basket.Id);
        resolution.Basket.Items.Single().Quantity.Should().Be(5);
        resolution.Basket.SubTotal.Should().Be(35m);
        (await ReceiptsAsync()).Single().IsCancelled.Should().BeFalse();
    }

    [Fact]
    public async Task An_applied_receipt_without_its_erased_basket_still_reports_applied_and_never_resurrects_it()
    {
        var request = AddRequest();
        var basket = await BasketAsync(await SendAsync("/api/customer-mobile/basket/items", request));
        using (var context = DatabaseFixture.CreateContext())
            await context.Baskets.Where(row => row.Id == basket.Id).ExecuteDeleteAsync();
        var resolution = await ResolutionAsync(await CancelAddAsync(request));
        resolution.Outcome.Should().Be(CustomerBasketOperationOutcome.Applied);
        resolution.Basket.Should().BeNull();
        (await SendAsync("/api/customer-mobile/basket/items", request)).StatusCode.Should().Be(HttpStatusCode.Conflict);
        using var verify = DatabaseFixture.CreateContext();
        (await verify.Baskets.CountAsync()).Should().Be(0);
        (await ReceiptsAsync()).Single().IsCancelled.Should().BeFalse();
    }

    [Theory]
    [InlineData(CustomerBasketItemAction.Update)]
    [InlineData(CustomerBasketItemAction.Remove)]
    public async Task Unseen_root_edits_and_removal_can_be_sealed_without_any_item_effect(CustomerBasketItemAction action)
    {
        var basket = await AddAsync();
        var request = Update(basket);
        if (action == CustomerBasketItemAction.Remove)
            request = request with { Action = action, Quantity = null, SpecialInstructions = null };
        var resolution = await ResolutionAsync(await CancelMutationAsync(basket, request));
        resolution.Outcome.Should().Be(CustomerBasketOperationOutcome.Cancelled);
        resolution.Basket.Should().BeNull();
        (await MutateAsync(basket, request)).StatusCode.Should().Be(HttpStatusCode.Conflict);
        using var context = DatabaseFixture.CreateContext();
        var stored = await context.Baskets.Include(row => row.Items).SingleAsync(row => row.Id == basket.Id);
        stored.Items.Single().Quantity.Should().Be(2);
        stored.Items.Single().SpecialInstructions.Should().BeNull();
        stored.SubTotal.Should().Be(14m);
        (await ReceiptsAsync()).Single().IsCancelled.Should().BeTrue();
    }

    [Fact]
    public async Task A_stale_refused_edit_can_be_sealed_before_a_fresh_reviewed_operation()
    {
        var original = await AddAsync();
        var stale = Update(original);
        var changed = await BasketAsync(await MutateAsync(original, Update(original)));
        (await MutateAsync(original, stale)).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ResolutionAsync(await CancelMutationAsync(original, stale))).Outcome.Should().Be(CustomerBasketOperationOutcome.Cancelled);
        (await MutateAsync(original, stale)).StatusCode.Should().Be(HttpStatusCode.Conflict);
        var fresh = Update(changed) with { Quantity = 4 };
        (await BasketAsync(await MutateAsync(changed, fresh))).Items.Single().Quantity.Should().Be(4);
        (await ReceiptsAsync()).Count.Should().Be(3);
    }

    [Fact]
    public async Task A_nil_basket_receipt_also_blocks_execution_when_read_without_the_new_cancellation_state()
    {
        var basket = await AddAsync();
        var request = Update(basket);
        await ResolutionAsync(await CancelMutationAsync(basket, request));
        // Model the prior executor's view: it does not read IsCancelled, but still checks the
        // original receipt's basket identity. This is a second barrier, not an old-binary run.
        using (var context = DatabaseFixture.CreateContext())
            await context.Set<RestaurantSystem.Domain.Entities.CustomerBasketOperation>()
                .Where(row => row.OperationId == request.OperationId)
                .ExecuteUpdateAsync(update => update.SetProperty(row => row.IsCancelled, false));
        (await MutateAsync(basket, request)).StatusCode.Should().Be(HttpStatusCode.Conflict);
        using var verify = DatabaseFixture.CreateContext();
        (await verify.BasketItems.SingleAsync()).Quantity.Should().Be(2);
        (await ReceiptsAsync()).Should().ContainSingle().Which.BasketId.Should().BeEmpty();
    }

    [Fact]
    public async Task Cancellation_keeps_exact_payload_kind_route_and_owner_identity()
    {
        var request = AddRequest();
        await ResolutionAsync(await CancelAddAsync(request));
        (await CancelAddAsync(request with { Quantity = 3 })).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await CancelAddAsync(request, Guid.NewGuid().ToString("D"))).StatusCode.Should().Be(HttpStatusCode.NotFound);
        var basket = await AddAsync();
        var root = Update(basket) with { OperationId = request.OperationId!.Value };
        (await CancelMutationAsync(basket, root)).StatusCode.Should().Be(HttpStatusCode.Conflict);
        var own = Update(basket);
        await ResolutionAsync(await CancelMutationAsync(basket, own));
        (await CancelMutationAsync(basket, own, itemId: Guid.NewGuid())).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ReceiptsAsync()).Count.Should().Be(2);
    }
}
