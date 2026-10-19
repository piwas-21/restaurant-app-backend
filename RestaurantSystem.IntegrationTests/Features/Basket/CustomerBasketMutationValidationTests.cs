using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using RestaurantSystem.Api.Features.Basket.Dtos.Requests;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Basket;

[Collection("Database Lane 2")]
public sealed class CustomerBasketMutationValidationTests(DatabaseFixture fixture) : CustomerBasketMutationFixture(fixture)
{
    [Theory]
    [InlineData("operation-empty")]
    [InlineData("basket-empty")]
    [InlineData("digest-invalid")]
    [InlineData("quantity-null")]
    [InlineData("quantity-zero")]
    [InlineData("quantity-negative")]
    [InlineData("quantity-excessive")]
    [InlineData("notes-excessive")]
    [InlineData("remove-quantity")]
    [InlineData("remove-notes")]
    [InlineData("unknown-action")]
    public async Task Invalid_or_ambiguous_edits_create_no_receipt(string invalid)
    {
        var basket = await AddAsync();
        var request = Update(basket);
        request = invalid switch
        {
            "operation-empty" => request with { OperationId = Guid.Empty },
            "basket-empty" => request with { ExpectedBasketId = Guid.Empty },
            "digest-invalid" => request with { ExpectedPurchaseFingerprint = "unreviewed" },
            "quantity-null" => request with { Quantity = null },
            "quantity-zero" => request with { Quantity = 0 },
            "quantity-negative" => request with { Quantity = -1 },
            "quantity-excessive" => request with { Quantity = 101 },
            "notes-excessive" => request with { SpecialInstructions = new string('x', 501) },
            "remove-quantity" => request with { Action = CustomerBasketItemAction.Remove, SpecialInstructions = null },
            "remove-notes" => request with { Action = CustomerBasketItemAction.Remove, Quantity = null },
            _ => request with { Action = (CustomerBasketItemAction)99 }
        };
        (await MutateAsync(basket, request)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReceiptsAsync()).Should().BeEmpty();
    }

    [Theory]
    [InlineData("operationId")]
    [InlineData("expectedBasketId")]
    [InlineData("expectedPurchaseFingerprint")]
    [InlineData("action")]
    public async Task Omitted_fields_cannot_silently_become_default_edits(string field)
    {
        var basket = await AddAsync();
        var body = JsonSerializer.SerializeToNode(Update(basket), JsonOptions)!.AsObject();
        body.Remove(field).Should().BeTrue();
        using var request = new HttpRequestMessage(HttpMethod.Post,
            new Uri($"/api/customer-mobile/basket/items/{basket.Items[0].Id}/operations", UriKind.Relative))
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json")
        };
        request.Headers.Add("X-Session-Id", Session);
        (await Client.SendAsync(request)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReceiptsAsync()).Should().BeEmpty();
    }

    [Theory]
    [InlineData("not-a-session")]
    [InlineData("AAAAAAAA-AAAA-4AAA-8AAA-AAAAAAAAAAAA")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public async Task Noncanonical_sessions_cannot_mutate_a_basket(string session)
    {
        var basket = await AddAsync();
        (await MutateAsync(basket, Update(basket), session: session)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReceiptsAsync()).Should().BeEmpty();
    }
}
