using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Basket.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Basket;

/// <summary>
/// The basket ROUTES and the missing-session contract, pinned at the HTTP layer because §9.7 moved
/// an action to a different controller and rewrote the guard that fronts seven of them.
/// </summary>
/// <remarks>
/// Nothing in the suite drove <c>PUT /api/Basket/order-type</c> over HTTP before this — the
/// order-type tests all go through the mediator or the service — so the URL was covered by nothing
/// at all. A controller split gets that wrong silently: a 404 on a route the client has always used
/// looks exactly like a client bug, and the C# would still compile and the handler tests still pass.
/// <para>
/// The bodies are asserted as a REFACTOR guard, not because a client parses them. §9.4's rule is the
/// reverse: "Session ID is required" is named there among the 400s whose message must never reach a
/// guest-facing toast, which is why this response carries no <c>ErrorCode</c>. What the assertions
/// pin is that <c>ApiResponse.Failure</c>'s shape survived — reason in <c>errors[0]</c>, generic
/// "Operation failed" in <c>message</c> — since a rewrite that "kept the 400" but moved the text
/// would be invisible to a status-code check.
/// </para>
/// </remarks>
[Collection("Database Lane 4")]
public class BasketRoutingContractTests : IntegrationTestBase
{
    private const string SessionRequired = "Session ID is required";

    public BasketRoutingContractTests(DatabaseFixture databaseFixture)
        : base(databaseFixture)
    {
    }

    [Fact]
    public async Task The_order_type_route_still_resolves_after_the_controller_split()
    {
        Client.DefaultRequestHeaders.Add("X-Session-Id", Guid.NewGuid().ToString());

        var response = await PutAsJsonAsync("/api/Basket/order-type", new
        {
            orderType = OrderType.Takeaway,
            removeConflicts = false
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "the URL is the contract — a 404 here is what a controller split gets wrong silently");
        var body = await ReadResponseAsync<ApiResponse<BasketChannelSwitchDto>>(response);
        // `Applied`, not `Success`: the handler answers SuccessWithData on BOTH branches, including
        // the refusal that returns conflicts and changes nothing — so `Success` cannot fail here.
        body!.Data!.Applied.Should().BeTrue("an empty cart upserts rather than 404ing (§9.13)");
    }

    /// <summary>
    /// The new controller must not ALSO be reachable at its class-derived route: the prefix lives on
    /// the shared base precisely so there is one spelling, and a stray <c>[Route("api/[controller]")]</c>
    /// would quietly serve the same action at a second URL.
    /// </summary>
    [Fact]
    public async Task The_channel_controller_is_not_reachable_at_its_class_name()
    {
        Client.DefaultRequestHeaders.Add("X-Session-Id", Guid.NewGuid().ToString());

        var response = await PutAsJsonAsync("/api/BasketChannel/order-type", new { orderType = OrderType.Takeaway });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// Covers every action the extracted guard now fronts, not just the header-only ones: the three
    /// that also take a body or a route parameter are where a dropped guard is least visible, and a
    /// discarded <c>MissingSession</c> call compiles.
    /// </summary>
    [Theory]
    [InlineData("GET", "/api/Basket")]
    [InlineData("GET", "/api/Basket/summary")]
    [InlineData("DELETE", "/api/Basket")]
    [InlineData("POST", "/api/Basket/items")]
    [InlineData("PUT", "/api/Basket/items/11111111-1111-1111-1111-111111111111")]
    [InlineData("DELETE", "/api/Basket/items/11111111-1111-1111-1111-111111111111")]
    public async Task Line_endpoints_still_refuse_a_missing_session_with_the_same_body(string method, string url)
    {
        var request = new HttpRequestMessage(new HttpMethod(method), url);
        if (method is "POST" or "PUT")
        {
            request.Content = JsonContent.Create(new { productId = Guid.NewGuid(), quantity = 1 });
        }

        var response = await Client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await ReadResponseAsync<ApiResponse<BasketDto>>(response);
        body!.Errors.Should().ContainSingle().Which.Should().Be(SessionRequired);
    }

    /// <summary>
    /// Pins the whole <c>[ApiController]</c>-by-inheritance story in one assertion. The attribute now
    /// sits on the abstract base, and if it stopped reaching a derived controller nothing else here
    /// would notice — every other test uses explicit binding sources and well-formed payloads. The
    /// consequence would not be a compile error: <c>SetBasketOrderTypeCommand.OrderType</c> is
    /// <c>[JsonRequired]</c>, whose enforcement IS the automatic model-state 400, so an empty body
    /// would silently bind the default channel and switch the basket to the wrong one.
    /// </summary>
    [Fact]
    public async Task An_empty_body_is_still_rejected_by_automatic_model_validation()
    {
        Client.DefaultRequestHeaders.Add("X-Session-Id", Guid.NewGuid().ToString());

        var response = await PutAsJsonAsync("/api/Basket/order-type", new { });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task The_order_type_endpoint_refuses_a_missing_session_with_the_same_body()
    {
        var response = await PutAsJsonAsync("/api/Basket/order-type", new { orderType = OrderType.Takeaway });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await ReadResponseAsync<ApiResponse<BasketChannelSwitchDto>>(response);
        body!.Errors.Should().ContainSingle().Which.Should().Be(SessionRequired);
    }

    /// <summary>
    /// The promo stubs went from <c>async</c>-without-<c>await</c> to synchronous, dropping two
    /// <c>#pragma warning disable CS1998</c> pairs. They are called by <c>basketService.ts</c>, which
    /// renders the message, so the response has to be byte-identical.
    /// </summary>
    [Fact]
    public async Task The_promo_stubs_still_answer_400_with_their_not_implemented_message()
    {
        Client.DefaultRequestHeaders.Add("X-Session-Id", Guid.NewGuid().ToString());

        var applied = await PostAsJsonAsync("/api/Basket/promo-code", new { promoCode = "SUMMER" });
        var removed = await Client.DeleteAsync("/api/Basket/promo-code");

        applied.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        removed.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        foreach (var stub in new[] { applied, removed })
        {
            (await ReadResponseAsync<ApiResponse<BasketDto>>(stub))!.Errors.Should()
                .ContainSingle().Which.Should().Be("Promo code functionality not yet implemented");
        }
    }
}
