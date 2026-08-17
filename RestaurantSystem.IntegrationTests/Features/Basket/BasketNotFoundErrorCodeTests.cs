using System.Net;
using FluentAssertions;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Basket.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Basket;

/// <summary>
/// <c>PUT|DELETE /api/Basket/items/{id}</c> answers a missing BASKET and a missing ITEM with the
/// same 404, and the client must do opposite things with them: a missing item is a benign resync
/// (the guest removed it in another tab), a missing basket is a real failure that has to be shown.
/// These tests pin the <c>errorCode</c> that separates them.
/// </summary>
/// <remarks>
/// The frontend used to separate them by substring-matching the English message, and
/// <c>"Basket not found".Contains("not found")</c> put the basket-level failure down the benign
/// branch: it resynced, <c>GetBasketQuery</c> answered the missing basket with an empty basket and
/// a SUCCESS, and one tap on "−" silently replaced the guest's whole cart with "Your cart is
/// empty" (frontend issue #415). A status-code assertion cannot see any of that — both paths are
/// 404 either way — so the code on the wire is the only thing worth pinning here.
/// <para>
/// Asserted on the RAW JSON as well as the parsed envelope, because the camelCase spelling is the
/// contract and the exception path does NOT go through <c>Program.cs</c>'s <c>AddJsonOptions</c>:
/// <c>ExceptionHandlingMiddleware</c> builds its own <c>JsonSerializerOptions</c> with only
/// <c>PropertyNamingPolicy = CamelCase</c> and serializes with that. A deserialize-and-assert alone
/// would pass on any spelling, since the test's own reader is case-insensitive.
/// </para>
/// <para>
/// Every request here is ANONYMOUS. That is load-bearing, not hygiene: <c>TestAuthHandler</c>
/// authenticates as the default Customer otherwise, and <c>BasketRepository.ApplyOwnerFilter</c>
/// keys the lookup on UserId alone whenever a user is present — so the <c>X-Session-Id</c> header
/// would satisfy the controller's guard and then be ignored, and the guest-with-a-reaped-session
/// scenario these codes exist for would never be exercised.
/// </para>
/// </remarks>
[Collection("Database Lane 3")]
public class BasketNotFoundErrorCodeTests : IntegrationTestBase
{
    private static readonly Guid AbsentItemId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    public BasketNotFoundErrorCodeTests(DatabaseFixture databaseFixture)
        : base(databaseFixture)
    {
    }

    [Fact]
    public async Task Updating_an_item_when_the_whole_basket_is_gone_is_coded_BasketNotFound()
    {
        UseFreshGuestSession();

        var response = await PutAsJsonAsync(
            $"/api/Basket/items/{AbsentItemId}",
            new { quantity = 1, specialInstructions = (string?)null });

        await AssertNotFoundWithCode(response, ErrorCodes.BasketNotFound, "Basket not found");
    }

    [Fact]
    public async Task Removing_an_item_when_the_whole_basket_is_gone_is_coded_BasketNotFound()
    {
        UseFreshGuestSession();

        var response = await Client.DeleteAsync($"/api/Basket/items/{AbsentItemId}");

        await AssertNotFoundWithCode(response, ErrorCodes.BasketNotFound, "Basket not found");
    }

    [Fact]
    public async Task Updating_an_absent_item_in_a_LIVE_basket_is_coded_BasketItemNotFound()
    {
        UseFreshGuestSession();
        await CreateBasketForSessionAsync();

        var response = await PutAsJsonAsync(
            $"/api/Basket/items/{AbsentItemId}",
            new { quantity = 1, specialInstructions = (string?)null });

        await AssertNotFoundWithCode(response, ErrorCodes.BasketItemNotFound, "Basket item not found");
    }

    [Fact]
    public async Task Removing_an_absent_item_from_a_LIVE_basket_is_coded_BasketItemNotFound()
    {
        UseFreshGuestSession();
        await CreateBasketForSessionAsync();

        var response = await Client.DeleteAsync($"/api/Basket/items/{AbsentItemId}");

        await AssertNotFoundWithCode(response, ErrorCodes.BasketItemNotFound, "Basket item not found");
    }

    /// <summary>
    /// An anonymous guest holding a brand-new session id — the identity the whole feature is about.
    /// See the note on the class: authenticated requests never consult the session id at all.
    /// </summary>
    private void UseFreshGuestSession()
    {
        AuthenticateAsAnonymous();
        Client.DefaultRequestHeaders.Remove("X-Session-Id");
        Client.DefaultRequestHeaders.Add("X-Session-Id", Guid.NewGuid().ToString());
    }

    /// <summary>
    /// Creates the basket ROW without needing a seeded product: §9.13 made the order-type endpoint
    /// upsert an empty basket, which is the only way to reach "basket exists, item does not"
    /// without going through the add path and its product fixtures. Keyed to the session id, since
    /// the caller is anonymous.
    /// </summary>
    private async Task CreateBasketForSessionAsync()
    {
        var created = await PutAsJsonAsync("/api/Basket/order-type", new
        {
            orderType = OrderType.Takeaway,
            removeConflicts = false
        });

        created.StatusCode.Should().Be(HttpStatusCode.OK,
            "the item-level assertions are only meaningful once the basket row really exists");
        var body = await ReadResponseAsync<ApiResponse<BasketChannelSwitchDto>>(created);
        body!.Data!.Applied.Should().BeTrue("an empty cart upserts rather than 404ing (§9.13)");
    }

    private async Task AssertNotFoundWithCode(
        HttpResponseMessage response,
        string expectedCode,
        string expectedMessage)
    {
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var rawJson = await response.Content.ReadAsStringAsync();
        rawJson.Should().Contain($"\"errorCode\":\"{expectedCode}\"",
            "the frontend branches on this exact camelCase code, not on the English message");

        var envelope = System.Text.Json.JsonSerializer.Deserialize<ApiResponse<BasketDto>>(rawJson, JsonOptions);
        envelope.Should().NotBeNull();
        envelope!.Success.Should().BeFalse();
        envelope.ErrorCode.Should().Be(expectedCode);
        // The English message stays as it was: older clients still read it, and the whole point of
        // the code is that it can now change (or be localised) without breaking anyone.
        envelope.Message.Should().Be(expectedMessage);
    }
}
