using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Common;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

/// <summary>
/// The order MUTATION endpoints are staff-only. They were <c>[Authorize]</c> — authenticated, but
/// unrestricted — so any signed-in customer could act on any order id: take a payment on it (there
/// is no gateway; the handler marks the payment Completed outright), cancel it (which force-refunds
/// every completed payment and emails the customer), drive its status, or toggle focus and read the
/// full <c>OrderDto</c> back as a side effect.
/// <para>
/// <b>Every one of these passed before the fix</b> — nothing in the suite exercised these endpoints
/// as a customer, which is exactly how the posture drifted. That is the reason this file exists at
/// all: an authorization change whose whole test suite stays green is indistinguishable from one
/// that did nothing.
/// </para>
/// <para>
/// The staff cases sit alongside deliberately: a blanket refusal would satisfy every customer
/// assertion here on its own, and silently break the cashier.
/// </para>
/// </summary>
public class OrderMutationAuthorizationTests : IntegrationTestBase
{
    private Guid _orderId;

    public OrderMutationAuthorizationTests(DatabaseFixture databaseFixture)
        : base(databaseFixture)
    {
    }

    public static TheoryData<string, string, object?> StaffOnlyEndpoints() => new()
    {
        { "POST", "payments", new { paymentMethod = "Cash", amount = 10.0m } },
        { "PUT", "focus", new { isFocusOrder = true, priority = 1, focusReason = "test" } },
        { "PUT", "status", new { newStatus = "Confirmed" } },
        { "POST", "cancel", new { cancellationReason = "test" } },
    };

    [Theory]
    [MemberData(nameof(StaffOnlyEndpoints))]
    public async Task A_customer_is_FORBIDDEN_from_mutating_an_order(string method, string action, object? body)
    {
        AuthenticateAsUser();

        var request = new HttpRequestMessage(new HttpMethod(method), $"/api/Orders/{_orderId}/{action}")
        {
            Content = JsonContent.Create(body)
        };
        var response = await Client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            $"{method} /api/Orders/{{id}}/{action} is a till/kitchen action, not something a guest may do to an order");
    }

    [Fact]
    public async Task A_customer_cannot_list_the_focus_queue()
    {
        // No ownership filter of any kind on this one — it returns every focused order.
        AuthenticateAsUser();

        var response = await Client.GetAsync("/api/Orders/focus");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    /// <summary>
    /// The control. Without it a blanket 403 would satisfy every assertion above while taking the
    /// cashier's till offline — and the cashier and admin surfaces are the ONLY callers of these
    /// endpoints (verified across the frontend), which is what makes staff-only non-breaking.
    /// </summary>
    [Fact]
    public async Task Staff_can_still_drive_an_order()
    {
        AuthenticateAsAdmin();

        var focus = await Client.PutAsJsonAsync(
            $"/api/Orders/{_orderId}/focus", new { isFocusOrder = true, priority = 1, focusReason = "service" });
        var queue = await Client.GetAsync("/api/Orders/focus");

        focus.StatusCode.Should().Be(HttpStatusCode.OK);
        queue.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>
    /// The refusal above is a status code; this is the thing that status code stands for. A gate
    /// that ran AFTER the handler had already written would satisfy every assertion above while
    /// the order was cancelled and the payment banked.
    /// </summary>
    [Fact]
    public async Task A_customers_refused_mutation_leaves_the_order_and_its_payments_untouched()
    {
        AuthenticateAsUser();

        await Client.PostAsJsonAsync($"/api/Orders/{_orderId}/cancel", new { cancellationReason = "let me in" });
        await Client.PostAsJsonAsync($"/api/Orders/{_orderId}/payments", new { paymentMethod = "Cash", amount = 10.0m });

        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var order = await context.Orders.AsNoTracking().FirstAsync(o => o.Id == _orderId);
        order.Status.Should().Be(OrderStatus.Pending, "the cancellation must not have been applied");
        order.CancellationReason.Should().BeNull();

        var payments = await context.OrderPayments.AsNoTracking().Where(p => p.OrderId == _orderId).ToListAsync();
        payments.Should().BeEmpty("no payment row may be written by a customer");
    }

    /// <summary>
    /// The control above authenticates as Admin, so it passes just as well under [RequireAdmin] —
    /// which would close the hole and take the cashier till, the kitchen display and the floor view
    /// down with it. These are the three roles that assertion cannot see.
    /// </summary>
    [Theory]
    [InlineData(UserRole.Cashier)]
    [InlineData(UserRole.KitchenStaff)]
    [InlineData(UserRole.Server)]
    public async Task Every_staff_role_can_still_drive_an_order(UserRole role)
    {
        AuthenticateAsRole(role);

        var status = await Client.PutAsJsonAsync(
            $"/api/Orders/{_orderId}/status", new { newStatus = "Confirmed" });

        status.StatusCode.Should().Be(HttpStatusCode.OK, $"{role} is back-of-house");
    }

    /// <summary>A caller with no credentials is challenged, not merely forbidden.</summary>
    [Fact]
    public async Task An_anonymous_caller_is_challenged_rather_than_forbidden()
    {
        AuthenticateAsAnonymous();

        var response = await Client.GetAsync("/api/Orders/focus");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    protected override async Task SeedTestData()
    {
        await base.SeedTestData();

        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        // Owned by the very customer who is refused below, so the assertions cannot be mistaken for
        // "a stranger's order is protected" — these endpoints are staff-only even for YOUR OWN order.
        var order = new Order
        {
            OrderNumber = "§9.19-MUTATE",
            UserId = Guid.Parse(TestAuthHandler.UserId),
            CustomerName = "Owner",
            Type = OrderType.Takeaway,
            Status = OrderStatus.Pending,
            PaymentStatus = PaymentStatus.Pending,
            OrderDate = DateTime.UtcNow,
            Total = 10m,
            CreatedBy = "test"
        };

        context.Add(order);
        await context.SaveChangesAsync();
        _orderId = order.Id;
    }
}
