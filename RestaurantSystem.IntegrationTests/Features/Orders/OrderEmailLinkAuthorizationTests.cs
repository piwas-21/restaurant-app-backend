using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Orders.Queries.GetOrdersQuery;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Infrastructure;
using System.Net;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

/// <summary>
/// Pins both halves of the quick-action email links: they must stay reachable with NO credentials
/// at all, and they must be reachable ONLY by someone holding the order's QuickActionToken.
///
/// #256 made the order mutation routes staff-only at the controller attribute. That placement is
/// load-bearing rather than stylistic, and nothing recorded why: OrderQuickActionsController
/// dispatches <c>UpdateOrderStatusCommand</c> and <c>CancelOrderCommand</c> from its
/// [AllowAnonymous] email-link actions, so the same rule expressed as an <c>IsStaff</c> check
/// INSIDE either handler — the obvious place, and where #258's ownership fix for the read route
/// lives — sees no authenticated user here and breaks every quick-confirm / quick-cancel link
/// already sitting in an inbox.
///
/// What #261 left open, and this file now covers: "anonymous" was the whole story. The routes were
/// keyed on the ORDER NUMBER, which is <c>yyyyMMdd</c> plus a counter restarting at 0001 daily, so
/// anyone could cancel a stranger's order — refunding its payments and mailing its customer — by
/// counting upwards, and read back each order's status from the response
/// (ORDER-TYPE-AVAILABILITY-PLAN §9.20). The negative cases below are the ones that fail if the
/// token check is removed or weakened to a non-secret.
/// </summary>
public class OrderEmailLinkAuthorizationTests : IntegrationTestBase
{
    public OrderEmailLinkAuthorizationTests(DatabaseFixture databaseFixture)
        : base(databaseFixture)
    {
    }

    private static readonly Guid QuickCancelOrderId = Guid.Parse("cccccccc-0000-0000-0000-000000000001");
    private static readonly Guid QuickConfirmOrderId = Guid.Parse("cccccccc-0000-0000-0000-000000000002");
    private static readonly Guid BystanderOrderId = Guid.Parse("cccccccc-0000-0000-0000-000000000003");

    // Distinct suffixes: these used to matter because the lookup matched the order number by
    // SUBSTRING and took the first row, so two numbers sharing a prefix selected each other's
    // order. The lookup is exact now; the names stay distinct because they read better.
    private const string QuickCancelOrderNumber = "ORDLINK-CANCEL";
    private const string QuickConfirmOrderNumber = "ORDLINK-CONFIRM";
    private const string BystanderOrderNumber = "ORDLINK-BYSTANDER";

    // Fixed rather than generated: a test that mints its own token via the production generator
    // passes even if the generator is replaced by a constant, which is the regression most worth
    // catching. These stand in for "whatever the email carried".
    private const string QuickCancelToken = "test-token-cancel-aaaaaaaaaaaaaaaaaaaaaaa";
    private const string QuickConfirmToken = "test-token-confirm-bbbbbbbbbbbbbbbbbbbbbb";
    private const string BystanderToken = "test-token-bystander-cccccccccccccccccccc";

    protected override async Task SeedTestData()
    {
        await base.SeedTestData();

        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        // Both actions only act on a Pending order. Separate rows so the tests cannot contend.
        // Guest orders (UserId null) — the quick-action links are emailed for orders placed
        // without an account too, and nothing on this path depends on an owner.
        context.Orders.AddRange(
            CreateOrder(QuickCancelOrderId, QuickCancelOrderNumber, QuickCancelToken),
            CreateOrder(QuickConfirmOrderId, QuickConfirmOrderNumber, QuickConfirmToken),
            CreateOrder(BystanderOrderId, BystanderOrderNumber, BystanderToken));

        await context.SaveChangesAsync();
    }

    /// <summary>
    /// CustomerEmail is deliberately null: both handlers fire a real SMTP send when it is set, and
    /// these tests are about reachability, not mail.
    /// </summary>
    private static Order CreateOrder(Guid id, string orderNumber, string quickActionToken) => new()
    {
        Id = id,
        OrderNumber = orderNumber,
        QuickActionToken = quickActionToken,
        UserId = null,
        CustomerName = "Jane Doe",
        CustomerEmail = null,
        OrderDate = new DateTime(2026, 4, 15, 12, 0, 0, DateTimeKind.Utc),
        Type = OrderType.Takeaway,
        Status = OrderStatus.Pending,
        PaymentStatus = PaymentStatus.Pending,
        SubTotal = 10m,
        Total = 10m,
        IsDeleted = false,
        CreatedAt = new DateTime(2026, 4, 15, 12, 0, 0, DateTimeKind.Utc),
        CreatedBy = "test",
    };

    [Fact]
    public async Task QuickCancelLink_StillCancels_WithNoCredentialsAtAll()
    {
        AuthenticateAsAnonymous();

        var response = await Client.GetAsync(
            $"/api/orders/{QuickCancelOrderNumber}/quick-cancel?token={QuickCancelToken}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Contain("Order Cancelled");
        (await LoadOrder(QuickCancelOrderId)).Status.Should().Be(OrderStatus.Cancelled,
            "the anonymous email-link path must keep reaching CancelOrderCommand");
    }

    /// <summary>
    /// The other half. Covered separately because quick-cancel alone leaves
    /// UpdateOrderStatusCommand unpinned, and that is the more-used of the two commands.
    /// minutes stays under the controller's 10-minute delay threshold so this lands on
    /// Confirmed rather than PendingApproval.
    /// </summary>
    [Fact]
    public async Task QuickConfirmLink_StillConfirms_WithNoCredentialsAtAll()
    {
        AuthenticateAsAnonymous();

        var response = await Client.GetAsync(
            $"/api/orders/{QuickConfirmOrderNumber}/quick-confirm?token={QuickConfirmToken}&minutes=5");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await LoadOrder(QuickConfirmOrderId)).Status.Should().Be(OrderStatus.Confirmed,
            "the anonymous email-link path must keep reaching UpdateOrderStatusCommand");
    }

    /// <summary>
    /// The enumeration attack itself: knowing only the order number — which is a printable daily
    /// counter — must not be enough to cancel someone's order.
    /// </summary>
    [Fact]
    public async Task QuickCancel_WithNoToken_LeavesTheOrderAlone()
    {
        AuthenticateAsAnonymous();

        var response = await Client.GetAsync($"/api/orders/{BystanderOrderNumber}/quick-cancel");

        await AssertRejected(response, BystanderOrderId);
    }

    [Fact]
    public async Task QuickCancel_WithWrongToken_LeavesTheOrderAlone()
    {
        AuthenticateAsAnonymous();

        var response = await Client.GetAsync(
            $"/api/orders/{BystanderOrderNumber}/quick-cancel?token=not-the-right-token");

        await AssertRejected(response, BystanderOrderId);
    }

    /// <summary>
    /// A token is scoped to the order it was minted for. Someone holding a legitimate link for
    /// their OWN order must not be able to point it at a different order number — the check has to
    /// compare against that order's stored token, not merely confirm the token exists.
    /// </summary>
    [Fact]
    public async Task QuickCancel_WithAnotherOrdersToken_LeavesTheOrderAlone()
    {
        AuthenticateAsAnonymous();

        var response = await Client.GetAsync(
            $"/api/orders/{BystanderOrderNumber}/quick-cancel?token={QuickConfirmToken}");

        await AssertRejected(response, BystanderOrderId);
    }

    [Fact]
    public async Task QuickConfirm_WithNoToken_LeavesTheOrderAlone()
    {
        AuthenticateAsAnonymous();

        var response = await Client.GetAsync($"/api/orders/{BystanderOrderNumber}/quick-confirm?minutes=5");

        await AssertRejected(response, BystanderOrderId);
    }

    /// <summary>
    /// A rejected link must not double as a status oracle: the response for "wrong token" has to be
    /// the same page as for "no such order", or the endpoint still answers "does order N exist?"
    /// for every N an attacker cares to try.
    /// </summary>
    [Fact]
    public async Task RejectedLink_IsIndistinguishableFrom_AnUnknownOrder()
    {
        AuthenticateAsAnonymous();

        const string unknownOrderNumber = "ORDLINK-DOES-NOT-EXIST";

        var wrongToken = await Client.GetAsync(
            $"/api/orders/{BystanderOrderNumber}/quick-cancel?token=not-the-right-token");
        var noSuchOrder = await Client.GetAsync(
            $"/api/orders/{unknownOrderNumber}/quick-cancel?token=not-the-right-token");

        wrongToken.StatusCode.Should().Be(noSuchOrder.StatusCode);

        // Both pages echo the order number the CALLER supplied, so they cannot be byte-equal.
        // That echo discloses nothing — it is the attacker's own input. Normalising it away and
        // then demanding exact equality is the real property: any OTHER difference between
        // "exists, wrong token" and "does not exist" is a usable oracle, and fails here.
        var rejected = (await wrongToken.Content.ReadAsStringAsync())
            .Replace(BystanderOrderNumber, "{orderNumber}", StringComparison.Ordinal);
        var unknown = (await noSuchOrder.Content.ReadAsStringAsync())
            .Replace(unknownOrderNumber, "{orderNumber}", StringComparison.Ordinal);

        rejected.Should().Be(unknown,
            "a real order with a bad token must be indistinguishable from an order that does not exist");
    }

    /// <summary>
    /// The compounding half of §9.20, asserted at the handler rather than over HTTP.
    ///
    /// <c>FindOrderByNumber</c> used to dispatch GetOrdersQuery in-process from this
    /// [AllowAnonymous] controller, and the guard read
    /// <c>!isStaff &amp;&amp; _currentUserService.UserId.HasValue</c> — so a caller with NO user
    /// matched neither branch and was handed the whole order book, unfiltered.
    ///
    /// This cannot be reached over HTTP: <c>GET /api/orders</c> is [Authorize], and every
    /// <c>AuthenticateAs*</c> helper supplies a NameIdentifier, so an HTTP test lands on the
    /// scoped branch that the OLD code already handled identically and passes with the fix
    /// reverted. Driving the handler with a genuinely anonymous <c>ICurrentUserService</c> is the
    /// only way to execute the added <c>Where(_ => false)</c> — and it also gives that predicate
    /// its only runtime exercise, so an EF translation failure surfaces here.
    /// </summary>
    [Fact]
    public async Task OrdersQuery_ForAnAnonymousCaller_ReturnsNothing_NotEveryOrder()
    {
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var handler = new GetOrdersQueryHandler(
            context,
            scope.ServiceProvider.GetRequiredService<IOrderMappingService>(),
            NullLogger<GetOrdersQueryHandler>.Instance,
            new AnonymousCurrentUser());

        var result = await handler.Handle(
            new GetOrdersQuery(
                Status: null, PaymentStatus: null, OrderType: null,
                StartDate: null, EndDate: null, UserId: null,
                Search: null, IsFocusOrder: null,
                OrderBy: "OrderDate", Descending: true,
                Page: 1, PageSize: 50),
            CancellationToken.None);

        // Guard the guard: if the seed ever stops producing rows this assertion passes vacuously.
        (await context.Orders.CountAsync()).Should().BeGreaterThan(0,
            "the emptiness below is only meaningful while there ARE orders to leak");

        result.Data!.TotalCount.Should().Be(0);
        result.Data.Items.Should().BeEmpty(
            "an unauthenticated in-process caller must get nothing, not every customer's order");
    }

    /// <summary>
    /// A caller with no identity at all — what <c>ICurrentUserService</c> resolves to inside a
    /// request to an [AllowAnonymous] route. <c>IsStaff</c> is the interface's default
    /// implementation over these values, deliberately left uncustomised so this stub cannot
    /// disagree with production about what "staff" means.
    /// </summary>
    private sealed class AnonymousCurrentUser : ICurrentUserService
    {
        public Guid? UserId => null;
        public string? UserName => null;
        public string? Email => null;
        public UserRole? Role => null;
        public bool IsAuthenticated => false;
        public bool IsAdmin => false;
        public Task<ApplicationUser?> GetUserAsync() => Task.FromResult<ApplicationUser?>(null);
    }

    private async Task AssertRejected(HttpResponseMessage response, Guid orderId)
    {
        // 200 with the not-found page rather than 401/404: these URLs open in a mail client, and
        // the controller's contract is an HTML status page. What matters is the DB state.
        (await response.Content.ReadAsStringAsync()).Should().Contain("Order Not Found");
        (await LoadOrder(orderId)).Status.Should().Be(OrderStatus.Pending,
            "an unauthenticated caller without the order's token must not be able to change it");
    }

    private async Task<Order> LoadOrder(Guid orderId)
    {
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await context.Orders.AsNoTracking().FirstAsync(o => o.Id == orderId);
    }
}
