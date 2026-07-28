using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Common;
using RestaurantSystem.IntegrationTests.Infrastructure;
using System.Net;
using System.Text;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

/// <summary>
/// Pins the role gate on the back-of-house order routes.
///
/// PR #258 closed the read half (GET /api/orders/{id}); these five were left
/// [Authorize]-only with handlers that loaded the order by id alone, so any
/// authenticated customer could read another customer's PII off the returned
/// OrderDto and — worse — cancel their order or post a payment against it.
///
/// The rule: these are staff routes, payments narrower still (till only), and no
/// customer reaches them at all — not even for an order they own.
/// </summary>
public class OrderMutationAuthorizationTests : IntegrationTestBase
{
    public OrderMutationAuthorizationTests(DatabaseFixture databaseFixture)
        : base(databaseFixture)
    {
    }

    private static readonly Guid OtherCustomerId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static readonly Guid OtherCustomerOrderId = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000001");
    private static readonly Guid OwnOrderId = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002");
    private static readonly Guid QuickCancelOrderId = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000003");

    private const string QuickCancelOrderNumber = "ORD-QUICK";

    // Route templates for the whole back-of-house set, so a gate that is dropped from any
    // one of them fails here rather than only on whichever route a test happened to name.
    // {0} is the order id; the focus queue takes none.
    private const string StatusRoute = "/api/orders/{0}/status";
    private const string CancelRoute = "/api/orders/{0}/cancel";
    private const string FocusRoute = "/api/orders/{0}/focus";
    private const string PaymentsRoute = "/api/orders/{0}/payments";
    private const string FocusQueueRoute = "/api/orders/focus";

    // Bodies are kept valid against each command's FluentValidation rules on purpose: a
    // request that 400s before the gate is reached would let these tests pass even with the
    // route wide open. Every forbidden case below is a well-formed request.
    private const string StatusBody = """{"newStatus":"Preparing"}""";
    private const string CancelBody = """{"cancellationReason":"customer changed their mind"}""";
    private const string FocusBody = """{"isFocusOrder":true,"priority":1,"focusReason":"VIP table"}""";
    private const string PaymentBody = """{"paymentMethod":"Cash","amount":10}""";

    protected override async Task SeedTestData()
    {
        await base.SeedTestData();

        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        // Order.UserId is a restricted FK to AspNetUsers, so the second customer must exist.
        context.Users.Add(new ApplicationUser
        {
            Id = OtherCustomerId,
            UserName = "other@example.com",
            NormalizedUserName = "OTHER@EXAMPLE.COM",
            Email = "other@example.com",
            NormalizedEmail = "OTHER@EXAMPLE.COM",
            EmailConfirmed = true,
            FirstName = "Other",
            LastName = "Customer",
            Role = UserRole.Customer,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "seed",
            RefreshToken = string.Empty,
            SecurityStamp = Guid.NewGuid().ToString()
        });

        context.Orders.AddRange(
            CreateOrder(OtherCustomerOrderId, "ORD-OTHER", OtherCustomerId, OrderStatus.Confirmed),
            CreateOrder(OwnOrderId, "ORD-OWN", Guid.Parse(TestAuthHandler.UserId), OrderStatus.Confirmed),
            // quick-cancel only acts on a Pending order.
            CreateOrder(QuickCancelOrderId, QuickCancelOrderNumber, OtherCustomerId, OrderStatus.Pending));

        await context.SaveChangesAsync();
    }

    /// <summary>
    /// CustomerEmail is deliberately left null: the cancel and status handlers fire a real
    /// SMTP send when it is set, and these tests are about the gate, not the mail. The PII
    /// this route used to hand out is pinned on the read path instead
    /// (<see cref="GetOrderByIdAuthorizationTests"/>).
    /// </summary>
    private static Order CreateOrder(Guid id, string orderNumber, Guid? userId, OrderStatus status) => new()
    {
        Id = id,
        OrderNumber = orderNumber,
        UserId = userId,
        CustomerName = "Jane Doe",
        CustomerEmail = null,
        CustomerPhone = "+41 79 000 00 00",
        OrderDate = new DateTime(2026, 4, 15, 12, 0, 0, DateTimeKind.Utc),
        Type = OrderType.Takeaway,
        Status = status,
        PaymentStatus = PaymentStatus.Pending,
        SubTotal = 10m,
        Total = 10m,
        IsDeleted = false,
        CreatedAt = new DateTime(2026, 4, 15, 12, 0, 0, DateTimeKind.Utc),
        CreatedBy = "test",
    };

    // ── the hole this closes ────────────────────────────────────────────

    [Theory]
    [InlineData("PUT", StatusRoute, StatusBody)]
    [InlineData("POST", CancelRoute, CancelBody)]
    [InlineData("PUT", FocusRoute, FocusBody)]
    [InlineData("POST", PaymentsRoute, PaymentBody)]
    [InlineData("GET", FocusQueueRoute, null)]
    public async Task Customer_OnAnyBackOfHouseRoute_IsForbidden(string method, string route, string? body)
    {
        AuthenticateAsUser();

        var response = await Send(method, route, OtherCustomerOrderId, body);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "a customer has no back-of-house surface and must not reach this route by id");
    }

    /// <summary>
    /// The status code alone would still pass if the gate ran after the handler had already
    /// written. This asserts the order itself is untouched — the property that actually matters.
    /// </summary>
    [Fact]
    public async Task Customer_CancellingAnotherCustomersOrder_LeavesTheOrderUntouched()
    {
        AuthenticateAsUser();

        var response = await Send("POST", CancelRoute, OtherCustomerOrderId, CancelBody);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var order = await LoadOrder(OtherCustomerOrderId);
        order.Status.Should().Be(OrderStatus.Confirmed, "the cancellation must not have been applied");
        order.CancellationReason.Should().BeNull();
    }

    /// <summary>
    /// Posting a payment against someone else's order was the sharpest edge of the hole:
    /// it writes a money row and can flip the order to Paid.
    /// </summary>
    [Fact]
    public async Task Customer_PayingAnotherCustomersOrder_RecordsNoPayment()
    {
        AuthenticateAsUser();

        var response = await Send("POST", PaymentsRoute, OtherCustomerOrderId, PaymentBody);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var payments = await context.OrderPayments
            .Where(p => p.OrderId == OtherCustomerOrderId)
            .ToListAsync();

        payments.Should().BeEmpty("no payment row may be written by a customer");
    }

    /// <summary>
    /// Deliberate tightening, pinned so it is not "fixed" back by accident: even the owner is
    /// refused. Cancelling your own order is not a shipped feature — no frontend surface calls
    /// this route, and the customer-initiated path is the emailed reject-delay link, which is
    /// anonymous and runs RejectDelayCommand instead. An owner branch here would be dead code.
    /// </summary>
    [Fact]
    public async Task Customer_CancellingTheirOwnOrder_IsAlsoForbidden()
    {
        AuthenticateAsUser();

        var response = await Send("POST", CancelRoute, OwnOrderId, CancelBody);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var order = await LoadOrder(OwnOrderId);
        order.Status.Should().Be(OrderStatus.Confirmed);
    }

    [Theory]
    [InlineData("PUT", StatusRoute, StatusBody)]
    [InlineData("POST", CancelRoute, CancelBody)]
    [InlineData("PUT", FocusRoute, FocusBody)]
    [InlineData("POST", PaymentsRoute, PaymentBody)]
    [InlineData("GET", FocusQueueRoute, null)]
    public async Task Anonymous_OnAnyBackOfHouseRoute_IsChallenged(string method, string route, string? body)
    {
        AuthenticateAsAnonymous();

        var response = await Send(method, route, OtherCustomerOrderId, body);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── the staff surfaces these routes exist for ───────────────────────

    /// <summary>
    /// Every staff role, not just Admin: gating these on [RequireAdmin] would close the hole
    /// and silently break the cashier till, the kitchen display and the floor view.
    /// </summary>
    [Theory]
    [InlineData(UserRole.Cashier)]
    [InlineData(UserRole.KitchenStaff)]
    [InlineData(UserRole.Server)]
    public async Task EveryStaffRole_CanAdvanceAnyOrdersStatus(UserRole role)
    {
        AuthenticateAsRole(role);

        var response = await Send("PUT", StatusRoute, OtherCustomerOrderId, StatusBody);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ReadResponseAsync<ApiResponse<OrderDto>>(response))!.Success.Should().BeTrue();
        (await LoadOrder(OtherCustomerOrderId)).Status.Should().Be(OrderStatus.Preparing);
    }

    [Fact]
    public async Task Admin_CancellingAnyOrder_StillWorks()
    {
        AuthenticateAsAdmin();

        var response = await Send("POST", CancelRoute, OtherCustomerOrderId, CancelBody);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await LoadOrder(OtherCustomerOrderId)).Status.Should().Be(OrderStatus.Cancelled);
    }

    [Fact]
    public async Task Staff_TogglingFocusAndReadingTheFocusQueue_StillWorks()
    {
        AuthenticateAsRole(UserRole.Cashier);

        var toggle = await Send("PUT", FocusRoute, OtherCustomerOrderId, FocusBody);
        toggle.StatusCode.Should().Be(HttpStatusCode.OK);

        var queue = await Client.GetAsync(FocusQueueRoute);
        queue.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await ReadResponseAsync<ApiResponse<List<OrderDto>>>(queue);
        result!.Data.Should().ContainSingle(o => o.Id == OtherCustomerOrderId);
    }

    /// <summary>Payments are the till's job, and the cashier is the surface that calls this.</summary>
    [Fact]
    public async Task Cashier_AddingAPayment_Succeeds()
    {
        AuthenticateAsRole(UserRole.Cashier);

        var response = await Send("POST", PaymentsRoute, OtherCustomerOrderId, PaymentBody);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await LoadOrder(OtherCustomerOrderId)).TotalPaid.Should().Be(10m);
    }

    /// <summary>
    /// Payments are gated tighter than the rest of the set ([RequireAdminOrCashier], matching
    /// the refund and z-report routes). Pinned so the narrower gate is not widened to
    /// [RequireStaff] for symmetry without someone deciding that on purpose.
    /// </summary>
    [Theory]
    [InlineData(UserRole.KitchenStaff)]
    [InlineData(UserRole.Server)]
    public async Task NonTillStaff_AddingAPayment_IsForbidden(UserRole role)
    {
        AuthenticateAsRole(role);

        var response = await Send("POST", PaymentsRoute, OtherCustomerOrderId, PaymentBody);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "no kitchen or floor surface takes payment; widening this needs a deliberate change");
    }

    // ── the reason the gate is on the controller, not in the handlers ───

    /// <summary>
    /// OrderQuickActionsController dispatches CancelOrderCommand from an [AllowAnonymous]
    /// email-link action. Had the fix gone into the handler as an IsStaff check — the obvious
    /// place, and where the read-path fix lives — it would have seen no authenticated user
    /// here and broken every quick-cancel link already sitting in an inbox. This is the test
    /// that fails if someone later "tidies" the gate down into the command handler.
    /// </summary>
    [Fact]
    public async Task EmailQuickCancelLink_StillCancelsWithNoCredentialsAtAll()
    {
        AuthenticateAsAnonymous();

        var response = await Client.GetAsync($"/api/orders/{QuickCancelOrderNumber}/quick-cancel");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Contain("Order Cancelled");
        (await LoadOrder(QuickCancelOrderId)).Status.Should().Be(OrderStatus.Cancelled,
            "the anonymous email-link path must keep reaching CancelOrderCommand");
    }

    // ── helpers ─────────────────────────────────────────────────────────

    private Task<HttpResponseMessage> Send(string method, string route, Guid orderId, string? body)
    {
        var uri = string.Format(System.Globalization.CultureInfo.InvariantCulture, route, orderId);
        var content = body is null ? null : new StringContent(body, Encoding.UTF8, "application/json");

        return method switch
        {
            "GET" => Client.GetAsync(uri),
            "PUT" => Client.PutAsync(uri, content),
            "POST" => Client.PostAsync(uri, content),
            _ => throw new ArgumentOutOfRangeException(nameof(method), method, "unsupported verb"),
        };
    }

    private async Task<Order> LoadOrder(Guid orderId)
    {
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await context.Orders.AsNoTracking().FirstAsync(o => o.Id == orderId);
    }
}
