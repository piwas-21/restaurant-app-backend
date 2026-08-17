using System.Net;
using System.Reflection;
using Microsoft.AspNetCore.RateLimiting;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.IntegrationTests.Common;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Payments;

/// <summary>
/// S9 (SOFRA-PAYMENTS-PLAN §5) — <c>GET /api/payments/checkout-status</c> through the real MVC
/// pipeline. The settle logic itself is S5's and is covered by
/// <see cref="SettleCheckoutSessionCommandHandlerTests"/>; what is untested until here is
/// everything BETWEEN a returning diner and that handler.
///
/// <para>
/// Every case seeds a session that is already TERMINAL, which is what lets these run against a host
/// with no Stripe configuration: the handler returns early for any non-<c>Created</c> session
/// without calling Stripe at all. Note what that does NOT prove — a session Stripe still reports
/// <c>open</c> stays <c>Created</c> and re-fetches on every call, which is exactly why the route
/// carries a (generous) rate-limit policy rather than none.
/// </para>
/// </summary>
public abstract class CheckoutStatusEndpointTestsBase : SettingsDrivenEndpointTest
{
    protected CheckoutStatusEndpointTestsBase(DatabaseFixture fixture) : base(fixture) { }

    protected async Task<HttpResponseMessage> Ask(string sessionId) =>
        await Client.GetAsync($"/api/payments/checkout-status?sessionId={Uri.EscapeDataString(sessionId)}");

    /// <summary>
    /// An order that has been through Stripe and a session already settled — the state the diner's
    /// browser lands on when the reconciler happened to win the race, and the state every repeat
    /// call sees.
    /// </summary>
    protected async Task<(string SessionId, string OrderNumber)> SeedSettledAsync(
        PaymentStatus paymentStatus = PaymentStatus.Completed,
        OrderStatus orderStatus = OrderStatus.Confirmed,
        CheckoutSessionStatus sessionStatus = CheckoutSessionStatus.Completed)
    {
        await using var seed = Fixture.CreateContext();

        var order = new Order
        {
            OrderNumber = $"S9-{Guid.NewGuid():N}"[..12],
            Type = OrderType.Takeaway,
            Status = orderStatus,
            PaymentStatus = paymentStatus,
            SubTotal = 16.90m,
            Total = 16.90m,
            CustomerEmail = "diner@example.com",
            OrderDate = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = nameof(CheckoutStatusEndpointTestsBase),
        };
        // The tender the order already carries. Its presence is what makes the repeat-call test
        // able to fail: a second settle would COMPLETE a second row beside it.
        order.Payments.Add(new OrderPayment
        {
            PaymentMethod = PaymentMethod.OnlinePayment,
            Amount = 16.90m,
            Status = paymentStatus == PaymentStatus.Completed ? PaymentStatus.Completed : PaymentStatus.Processing,
            PaymentDate = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = nameof(CheckoutStatusEndpointTestsBase),
        });
        seed.Orders.Add(order);
        // Order.Id is assigned by EF at SAVE time, not at Add, so the session's FK needs this first.
        await seed.SaveChangesAsync();

        var sessionId = $"cs_test_{Guid.NewGuid():N}";
        seed.OrderCheckoutSessions.Add(new OrderCheckoutSession
        {
            OrderId = order.Id,
            SessionId = sessionId,
            Status = sessionStatus,
            Currency = "chf",
            AmountMinor = 1690,
            IdempotencyKey = $"checkout:{order.Id}:1",
            ExpiresAt = DateTime.UtcNow.AddMinutes(31),
            ConnectedAccountId = "acct_test_connected",
            CreatedAt = DateTime.UtcNow,
            CreatedBy = nameof(CheckoutStatusEndpointTestsBase),
        });
        await seed.SaveChangesAsync();

        return (sessionId, order.OrderNumber);
    }
}

/// <summary>
/// The rate-limit policy, pinned by REFLECTION rather than by firing 121 requests.
///
/// It exists for one reason and it is easy to lose: a session Stripe still reports <c>open</c>
/// stays <c>Created</c>, so every call on it re-fetches from Stripe — an anonymous amplifier of
/// reads against the tenant's connected account. The first cut of this endpoint shipped with NO
/// policy on an argument that was simply wrong ("bounded at one Stripe call per session"), and
/// nothing in the suite would have noticed either way.
/// </summary>
public class CheckoutStatusRateLimitTests
{
    [Fact]
    public void The_return_trip_carries_its_own_generous_policy()
    {
        var action = typeof(RestaurantSystem.Api.Features.Payments.PaymentsController)
            .GetMethod("GetCheckoutStatus")!;

        var policy = action.GetCustomAttribute<EnableRateLimitingAttribute>();

        policy.Should().NotBeNull("an unlimited checkout-status lets one minted session amplify Stripe reads");
        // Its OWN policy, not the minting one: a diner who spent their checkout-session permits
        // retrying must still be able to learn whether their money arrived.
        policy!.PolicyName.Should().Be("checkout-status");
    }

    [Fact]
    public void It_is_not_the_minting_endpoints_bucket()
    {
        var mint = typeof(RestaurantSystem.Api.Features.Payments.PaymentsController)
            .GetMethod("CreateCheckoutSession")!
            .GetCustomAttribute<EnableRateLimitingAttribute>();

        mint!.PolicyName.Should().Be("checkout-session");
    }
}

/// <summary>The ordinary case: the tenant bought the module, so the route is reachable.</summary>
[Collection("Database Lane 2")]
public class CheckoutStatusReachableTests : CheckoutStatusEndpointTestsBase
{
    public CheckoutStatusReachableTests(DatabaseFixture fixture) : base(fixture) { }

    protected override IReadOnlyDictionary<string, string> Settings => new Dictionary<string, string>
    {
        ["Modules:Enforce"] = "true",
        ["Modules:Enabled"] = "core,online-payments",
    };

    [Fact]
    public async Task A_returning_diner_is_told_where_the_order_stands()
    {
        var (sessionId, orderNumber) = await SeedSettledAsync();

        var response = await Ask(sessionId);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var data = await ReadData(response);
        data.GetProperty("orderNumber").GetString().Should().Be(orderNumber);
        // Enum NAMES, matching the wire contract the frontend union is written against.
        data.GetProperty("paymentStatus").GetString().Should().Be(nameof(PaymentStatus.Completed));
        data.GetProperty("orderStatus").GetString().Should().Be(nameof(OrderStatus.Confirmed));
    }

    [Fact]
    public async Task It_reports_an_expired_session_honestly_rather_than_pretending_success()
    {
        // The diner who sat on Stripe past the 31 minutes, or whose session S7 swept. Landing on a
        // "thank you" page here would be the worst possible lie — so the endpoint must hand the
        // frontend the real statuses and let it say so.
        var (sessionId, orderNumber) = await SeedSettledAsync(
            PaymentStatus.Pending, OrderStatus.Cancelled, CheckoutSessionStatus.Expired);

        var response = await Ask(sessionId);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var data = await ReadData(response);
        data.GetProperty("orderNumber").GetString().Should().Be(orderNumber);
        data.GetProperty("paymentStatus").GetString().Should().Be(nameof(PaymentStatus.Pending));
        data.GetProperty("orderStatus").GetString().Should().Be(nameof(OrderStatus.Cancelled));
    }

    [Fact]
    public async Task Repeating_the_call_on_a_settled_session_is_a_stable_200()
    {
        // NAMED for what it pins, after review caught the first version overclaiming. A diner
        // refreshes, and S7's reconciler may have settled this session from the other side, so a
        // repeat must not 500 and must not contradict itself.
        //
        // It is NOT the idempotency test, and the two row-count assertions it used to carry could
        // not fail: this fixture seeds a TERMINAL session, so both calls take the handler's early
        // return and never reach the claim. That is the shape plan §6c already warned about — "a
        // sequential second settle never reaches the atomic claim" — and the real coverage is the
        // mutation-checked conditional UPDATE in SettleCheckoutSessionCommandHandlerTests.
        var (sessionId, orderNumber) = await SeedSettledAsync();

        var first = await Ask(sessionId);
        var second = await Ask(sessionId);

        first.StatusCode.Should().Be(HttpStatusCode.OK);
        second.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ReadData(first)).GetProperty("orderNumber").GetString().Should().Be(orderNumber);
        (await ReadData(second)).GetProperty("orderNumber").GetString().Should().Be(orderNumber);

        await using var verify = Fixture.CreateContext();
        // A second tender IS something a broken repeat could produce, unlike the order and session
        // counts this replaced — nothing on this endpoint can insert either of those.
        (await verify.OrderPayments.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task An_unknown_session_is_404_and_not_a_500()
    {
        var response = await Ask("cs_test_nothing_here");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task An_absent_session_id_is_refused_by_the_validator()
    {
        var response = await Ask(string.Empty);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task An_oversized_session_id_is_refused_before_it_reaches_a_query()
    {
        // The column is varchar(255) and the lookup is an equality match, so a longer value cannot
        // match anything that exists. The validator's bound keeps it out of the database rather
        // than letting it travel there to find nothing.
        var response = await Ask(new string('x', 300));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}

/// <summary>
/// The tenant did not buy the module. The class-level gate must refuse BEFORE the handler runs —
/// asserted with a real, resolvable session seeded, so a 404 here cannot be the lookup missing.
/// </summary>
[Collection("Database Lane 2")]
public class CheckoutStatusUnboughtModuleTests : CheckoutStatusEndpointTestsBase
{
    public CheckoutStatusUnboughtModuleTests(DatabaseFixture fixture) : base(fixture) { }

    protected override IReadOnlyDictionary<string, string> Settings => new Dictionary<string, string>
    {
        ["Modules:Enforce"] = "true",
        ["Modules:Enabled"] = "core,cashier",
    };

    [Fact]
    public async Task An_unbought_module_answers_404_before_the_session_is_ever_looked_up()
    {
        var (sessionId, _) = await SeedSettledAsync();

        var response = await Ask(sessionId);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        // The discriminator: a routing/lookup 404 carries no error code, the module gate's does.
        (await ReadErrorCode(response)).Should().Be(ErrorCodes.ModuleNotEnabled);
    }
}
